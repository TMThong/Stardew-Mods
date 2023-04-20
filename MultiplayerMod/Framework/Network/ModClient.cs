using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using StardewValley;
using StardewModdingAPI;
using StardewValley.Network;
using StardewValley.Menus;
using System.Net;
using System.Threading;
using System.Diagnostics;
using StardewModdingAPI.Events;
using Microsoft.Xna.Framework;
using Thong.Net;
using TClient = Thong.Net.Client;
using TServer = Thong.Net.Server;
using StardewValley.Locations;
using StardewValley.Objects;

namespace MultiplayerMod.Framework.Network
{

    internal sealed class ModClient : StardewValley.Network.Client, IHandleMessage
    {


        private TClient Client = new TClient();
        public String host;
        public Config ModConfig;
        private object LookObject = new object();
        private List<IncomingMessage> Messages = new List<IncomingMessage>();
        internal ModClient(Config config, string h)
        {
            ModConfig = config;
            host = h;
            Client.Handle = this;
            if (host.Contains(":"))
            {
                string[] strings = host.Split(':');
                host = strings[0];
                config.Port = int.Parse(strings[1]);
                ModUtilities.Helper.WriteConfig(config);
            }
        }

        public void OnHandle(Message message)
        {
            IncomingMessage incomingMessage = new IncomingMessage();
            incomingMessage.Read(message.Reader);
            Messages.Add(incomingMessage);
            ModUtilities.ModMonitor.Log("receiveMessagesImpl Message " + incomingMessage.MessageType, LogLevel.Warn);
        }

        public override void sendMessage(OutgoingMessage message)
        {

            Message message1 = new Message(0);
            message.Write(message1.Writer);
            message1.Writer.Close();
            Client.SendMessage(message1);

        }

        protected override void receiveMessagesImpl()
        {
            while (Messages.Count > 0)
            {
                foreach (IncomingMessage message in Messages.ToArray())
                {
                    Messages.Remove(message);
                    processIncomingMessage(message);
                }
            }
        }
        public override void disconnect(bool neatly = true)
        {
            if (Client.IsConnected)
            {
                Client.Disconnect();
                Game1.ExitToTitle();
            }
            else
            {
                Game1.ExitToTitle();
            }

        }
        protected override void connectImpl()
        {
            new Thread(() =>
            {
                try
                {
                    ModUtilities.ModMonitor.Log($"Try connect to server , result = {Client.IsConnected}", LogLevel.Alert);
                    Client.Connect(host, ModConfig.Port);
                    Client.Start();
                    ModUtilities.ModMonitor.Log($"Try connect to server , result = {Client.IsConnected}", LogLevel.Alert);
                }
                catch (Exception e)
                {
                    ModUtilities.ModMonitor.Log($"Connection to {host} failed", LogLevel.Warn);
                }
                if (!Client.IsConnected)
                {
                    this.timedOut = true;
                }
            })
            { IsBackground = true }.Start();

        }
        public override string getUserID()
        {
            return "";
        }
        protected override string getHostUserName()
        {
            return Client.TcpClient.Client.RemoteEndPoint.ToString();
        }

         
    }
}