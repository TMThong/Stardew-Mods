/** Counter names exposed on `/metrics`. */
export const COUNTER_NAMES = [
  "connections_total",
  "connections_rejected_total",
  "rooms_created_total",
  "rooms_closed_total",
  "rooms_expired_total",
  "joins_total",
  "join_failures_total",
  "messages_received_total",
  "messages_relayed_total",
  "invalid_messages_total",
  "rate_limited_total",
  "heartbeat_timeouts_total"
] as const;

export type CounterName = (typeof COUNTER_NAMES)[number];

export interface MetricsGauges {
  rooms: number;
  peers: number;
  connections: number;
}

/** Tiny in-process counter registry - no external metrics dependency. */
export class Metrics {
  private readonly counters: Record<CounterName, number>;
  public readonly startedAt = Date.now();

  public constructor() {
    this.counters = Object.fromEntries(COUNTER_NAMES.map((name) => [name, 0])) as Record<CounterName, number>;
  }

  public increment(name: CounterName, amount = 1): void {
    this.counters[name] += amount;
  }

  public get(name: CounterName): number {
    return this.counters[name];
  }

  public snapshot(): Record<CounterName, number> {
    return { ...this.counters };
  }

  /** Renders the Prometheus text exposition format (version 0.0.4). */
  public toPrometheus(gauges: MetricsGauges): string {
    const lines: string[] = [];

    for (const name of COUNTER_NAMES) {
      lines.push(`# TYPE svdconnect_${name} counter`);
      lines.push(`svdconnect_${name} ${this.counters[name]}`);
    }

    const gaugeEntries: Array<[string, number]> = [
      ["active_rooms", gauges.rooms],
      ["active_peers", gauges.peers],
      ["active_connections", gauges.connections],
      ["uptime_seconds", Math.floor((Date.now() - this.startedAt) / 1000)]
    ];

    for (const [name, value] of gaugeEntries) {
      lines.push(`# TYPE svdconnect_${name} gauge`);
      lines.push(`svdconnect_${name} ${value}`);
    }

    return `${lines.join("\n")}\n`;
  }
}
