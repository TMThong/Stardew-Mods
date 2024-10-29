using Microsoft.AspNetCore.SignalR.Client;
using StardewValley;
using StardewValley.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerMod.Framework.Network
{
    public class SignalRHostClient : StardewValley.Network.Server
    {
        public string HubURL { get; }

        public string HostKey { get; }

        public string RoomId { get; }

        public string Password { get; }

        public HubConnection Connection { get; protected set; }


        protected SignalRHostClient(IGameServer gameServer) : base(gameServer)
        {

        }

        public override int connectionsCount => throw new NotImplementedException();

        public override bool connected()
        {
            return Connection?.State == HubConnectionState.Connected;
        }

        public override string getUserName(long farmerId)
        {
            throw new NotImplementedException();
        }

        public override void initialize()
        {
            this.Connection = new HubConnectionBuilder().WithUrl(HubURL).WithAutomaticReconnect().Build();
            this.Connection.StartAsync().Wait();
        }

        public override void receiveMessages()
        {
             
        }

        public override void sendMessage(long peerId, OutgoingMessage message)
        {
             
        }

        public override void setLobbyData(string key, string value)
        {
             
        }

        public override void setPrivacy(ServerPrivacy privacy)
        {
             
        }

        public override void stopServer()
        {
            this.Connection?.StopAsync().Wait();
            this.Connection?.DisposeAsync();
            this.Connection = null;
        }
    }
}
