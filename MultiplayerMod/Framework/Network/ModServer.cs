using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Thong.Net;
using TClient = Thong.Net.Client;
using TServer = Thong.Net.Server;
using StardewValley;
using StardewModdingAPI;
using StardewValley.Network;
using System.Net;
using System.Net.Sockets;
using Netcode;
using System.Threading;
using Microsoft.Xna.Framework;
namespace MultiplayerMod.Framework.Network
{

    internal sealed class ModServer : StardewValley.Network.Server, IServerHandle
    {
        public TServer Server { get; }

        public Dictionary<long, TClient> PeerConnection { get; } = new Dictionary<long, TClient>();

        public List<TClient> PlayerConnection { get; } = new List<TClient>();

        internal List<ClientIncomingMessage> IncomingMessage { get; } = new List<ClientIncomingMessage>();
        public ModServer(IGameServer gameServer, Config config) : base(gameServer)
        {
            Server = new TServer(config.Port);
            Server.ServerHandle = this;

        }

        public class ClientHandleMessage : IHandleMessage
        {
            public TClient Client { get; set; }
            public ModServer ModServer { get; set; }
            public ClientHandleMessage(TClient tClient, ModServer server)
            {
                Client = tClient;
                ModServer = server;
            }
            public void OnHandle(Message message)
            {
                ModServer.IncomingMessage.Add(new MultiplayerMod.Framework.Network.ClientIncomingMessage(message, Client));
            }

            public void OnDisconected()
            {

            }
        }
        public void ClientConnected(TClient tClient)
        {
            tClient.Handle = new ClientHandleMessage(tClient, this);
        }
        public void ClientDisconnected(TClient tClient)
        {
            foreach (var k in PeerConnection.Keys)
            {
                if (PeerConnection[k] == tClient)
                {
                    playerDisconnected_(k);
                }
            }
#if DEBUG
            ModUtilities.ModMonitor.Log("Server receive client disconnected", LogLevel.Warn);
#endif
        }
        public override int connectionsCount
        {
            get
            {
                return Server.Clients.Count;
            }
        }
        public override bool connected()
        {
            return Server.IsRunning;
        }
        public override void initialize()
        {
            Server.Start();
            ModUtilities.ModMonitor.Log($"Server is running with port {this.Server.Port}...", LogLevel.Alert);
        }
        public override void stopServer()
        {
            try
            {

                Server.Stop();
                PeerConnection.Clear();
                PlayerConnection.Clear();
            }
            catch (Exception e)
            {

            }

        }

        public override void setLobbyData(string key, string value)
        {

        }
        public override void setPrivacy(ServerPrivacy privacy)
        {

        }
        public override void receiveMessages()
        {

            foreach (var m in IncomingMessage.ToList())
            {
                if (m != null)
                {
                    this.ReceivedIncomingMessage(m.message.ReadIncomingMessage(), m.sender);
                }
                IncomingMessage.Remove(m);
            }

            foreach (TClient conn in Server.Clients.ToList())
            {

                if (!PlayerConnection.Contains(conn))
                {
                    if (!gameServer.whenGameAvailable(delegate
                    {
                        gameServer.sendAvailableFarmhands("", delegate (OutgoingMessage msg)
                        {
                            sendMessage(conn, msg);
                        });
                    }, () => Game1.gameMode != 6))
                    {
                        ModUtilities.ModMonitor.Log("Postponing introduction message", LogLevel.Info);

                        sendMessage(conn, new OutgoingMessage(11, Game1.player, "Strings\\UI:Client_WaitForHostLoad"));
                    }
                    PlayerConnection.Add(conn);
                }
            }
        }

        public void kick_(long disconnectee)
        {
            playerDisconnected(disconnectee);
        }
        public void playerDisconnected_(long disconnectee)
        {

            if (PeerConnection.ContainsKey(disconnectee))
            {
                PeerConnection[disconnectee].Disconnect();
                PeerConnection.Remove(disconnectee);
                PlayerConnection.Remove(PeerConnection[disconnectee]);
            }
        }
        public void ReceivedIncomingMessage(IncomingMessage message, TClient connection)
        {
#if DEBUG
            ModUtilities.ModMonitor.Log("Server receive " + message.MessageType, LogLevel.Warn);
#endif
            if (PeerConnection.ContainsKey(message.FarmerID) && PeerConnection[message.FarmerID] == connection)
            {
                gameServer.processIncomingMessage(message);
            }
            else if (message.MessageType == 2)
            {
                NetFarmerRoot farmer = ModUtilities.multiplayer.readFarmer(message.Reader);
                gameServer.checkFarmhandRequest("", getConnectionId(connection), farmer, delegate (OutgoingMessage msg)
                {
                    sendMessage(connection, msg);
                }, delegate
                {
                    PeerConnection[farmer.Value.UniqueMultiplayerID] = connection;
                });
            }
        }
        public override string getUserName(long farmerId)
        {
            if (PeerConnection.ContainsKey(farmerId))
            {
                return PeerConnection[farmerId].TcpClient.Client.RemoteEndPoint.ToString();
            }
            return "";
        }
        public override void sendMessage(long peerId, OutgoingMessage message)
        {
            if (PeerConnection.ContainsKey(peerId))
            {
                sendMessage(PeerConnection[peerId], message);
            }
        }
        public void sendMessage(TClient connection, OutgoingMessage outgoingMessage)
        {
            Message message1 = new Message(0);
            outgoingMessage.Write(message1.Writer);
            message1.Writer.Close();
            connection.SendMessage(message1);
#if DEBUG
            ModUtilities.ModMonitor.Log("Server send " + outgoingMessage.MessageType, LogLevel.Warn);
#endif
        }
        public override bool canAcceptIPConnections()
        {
            return true;

        }
        public string getConnectionId(TClient connection)
        {
            return "MultiplayerMod-" + connection.TcpClient.Client.RemoteEndPoint.ToString();
        }
        public override bool isConnectionActive(string connectionId)
        {
            foreach (TClient connection in Server.Clients.ToList())
            {
                if (getConnectionId(connection) == connectionId)
                {
                    return true;
                }
            }
            return false;
        }
        public override bool hasUserId(string userId)
        {
            foreach (TClient connection in Server.Clients.ToList())
            {
                if (connection.TcpClient.Client.RemoteEndPoint.ToString() == userId)
                {
                    return true;
                }
            }
            return false;
        }
        public override string getUserId(long farmerId)
        {
            if (!this.PeerConnection.ContainsKey(farmerId))
            {
                return null;
            }
            return this.PeerConnection[farmerId].TcpClient.Client.RemoteEndPoint.ToString();
        }


        ~ModServer()
        {
            try
            {
                if (Server.IsRunning)
                {
                    stopServer();
                }
            }
            catch { }
        }
    }
}