using Microsoft.AspNetCore.SignalR.Client;
using StardewValley.Network;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerMod.Framework.Network
{
    public class SignalRClient : StardewValley.Network.Client
    {
        public string HubURL { get; }

        public string HostKey { get; }

        public string RoomId { get; }

        public string Password { get; }

        public HubConnection Connection { get; protected set; }

        public override void disconnect(bool neatly = true)
        {
            this.Connection?.StopAsync().Wait();
            this.Connection?.DisposeAsync();
            this.Connection = null;
        }

        public override string getUserID()
        {
            throw new NotImplementedException();
        }

        public override void sendMessage(OutgoingMessage message)
        {
            throw new NotImplementedException();
        }

        protected override void connectImpl()
        {
            this.Connection = new HubConnectionBuilder().WithUrl(HubURL).WithAutomaticReconnect().Build();
            this.Connection.StartAsync().Wait();
        }

        protected override string getHostUserName()
        {
            throw new NotImplementedException();
        }

        protected override void receiveMessagesImpl()
        {

        }
    }
}
