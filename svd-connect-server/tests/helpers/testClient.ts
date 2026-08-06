import { WebSocket } from "ws";

export interface AnyMessage {
  type: string;
  [key: string]: unknown;
}

/** Minimal promise-based WebSocket client used by the integration tests. */
export class TestClient {
  private readonly received: AnyMessage[] = [];
  private readonly waiters: Array<{ predicate: (message: AnyMessage) => boolean; resolve: (m: AnyMessage) => void }> =
    [];

  public closeCode: number | null = null;
  public closeReason = "";

  private constructor(public readonly socket: WebSocket) {
    socket.on("message", (data) => {
      const message = JSON.parse(data.toString()) as AnyMessage;
      this.received.push(message);
      for (let index = this.waiters.length - 1; index >= 0; index -= 1) {
        const waiter = this.waiters[index];
        if (waiter !== undefined && waiter.predicate(message)) {
          this.waiters.splice(index, 1);
          waiter.resolve(message);
        }
      }
    });
    socket.on("close", (code, reason) => {
      this.closeCode = code;
      this.closeReason = reason.toString();
    });
  }

  public static connect(url: string, options: { headers?: Record<string, string> } = {}): Promise<TestClient> {
    return new Promise((resolve, reject) => {
      const socket = new WebSocket(url, options.headers === undefined ? {} : { headers: options.headers });
      const client = new TestClient(socket);
      socket.once("open", () => resolve(client));
      socket.once("error", (error) => reject(error));
    });
  }

  public send(message: unknown): void {
    this.socket.send(JSON.stringify(message));
  }

  public sendRaw(payload: string | Buffer): void {
    this.socket.send(payload);
  }

  /** Resolves with the first (buffered or future) message matching `type`. */
  public waitFor(type: string, timeoutMs = 5000): Promise<AnyMessage> {
    return this.waitWhere((message) => message.type === type, timeoutMs, `type=${type}`);
  }

  public waitWhere(
    predicate: (message: AnyMessage) => boolean,
    timeoutMs = 5000,
    description = "predicate"
  ): Promise<AnyMessage> {
    const buffered = this.received.find(predicate);
    if (buffered !== undefined) return Promise.resolve(buffered);

    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => {
        const index = this.waiters.findIndex((waiter) => waiter.resolve === wrapped);
        if (index >= 0) this.waiters.splice(index, 1);
        reject(
          new Error(
            `Timed out waiting for ${description}. Received: ${this.received.map((m) => m.type).join(", ") || "<nothing>"}`
          )
        );
      }, timeoutMs);

      const wrapped = (message: AnyMessage): void => {
        clearTimeout(timer);
        resolve(message);
      };

      this.waiters.push({ predicate, resolve: wrapped });
    });
  }

  public waitForClose(timeoutMs = 5000): Promise<number> {
    if (this.socket.readyState === WebSocket.CLOSED) return Promise.resolve(this.closeCode ?? 1006);
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => reject(new Error("Timed out waiting for close")), timeoutMs);
      this.socket.once("close", (code) => {
        clearTimeout(timer);
        resolve(code);
      });
    });
  }

  public get messages(): readonly AnyMessage[] {
    return this.received;
  }

  public has(type: string): boolean {
    return this.received.some((message) => message.type === type);
  }

  public close(): Promise<void> {
    if (this.socket.readyState === WebSocket.CLOSED) return Promise.resolve();
    return new Promise((resolve) => {
      this.socket.once("close", () => resolve());
      this.socket.close();
    });
  }
}

/** Small helper so tests can assert on "nothing arrived within N ms". */
export function delay(ms: number): Promise<void> {
  return new Promise((resolve) => setTimeout(resolve, ms));
}
