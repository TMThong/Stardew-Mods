using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Thong.Net;
using TClient = Thong.Net.Client;
namespace MultiplayerMod.Framework.Network
{
    internal class ClientIncomingMessage
    {
        public Message message;
        public TClient sender;

        public ClientIncomingMessage(Message message, TClient sender)
        {
            this.message = message;
            this.sender = sender;
        }
    }
}
