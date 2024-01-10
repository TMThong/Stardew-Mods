using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using MultiplayerMod.Framework.Patch.Mobile;
using Netcode;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Network;
using StardewValley.Objects;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MultiplayerMod.Framework.Mobile
{
    internal class MobileMultiplayer : Multiplayer
    {
        public virtual int MaxPlayers
        {
            get
            {
                if (Game1.server == null)
                {
                    return 1;
                }
                return this.playerLimit;
            }
        }

        public virtual bool isDisconnecting(Farmer farmer)
        {
            return this.isDisconnecting(farmer.UniqueMultiplayerID);
        }

        public virtual bool isDisconnecting(long uid)
        {
            return this.disconnectingFarmers.Contains(uid);
        }

        public virtual bool isClientBroadcastType(byte messageType)
        {
            switch (messageType)
            {
                case 0:
                case 2:
                case 4:
                case 6:
                case 7:
                case 12:
                case 13:
                case 14:
                case 15:
                case 19:
                case 20:
                case 21:
                case 22:
                case 24:
                case 26:
                    return true;
            }
            return false;
        }

        public virtual bool allowSyncDelay()
        {
            return Game1.newDaySync == null;
        }

        public virtual int interpolationTicks()
        {
            if (!this.allowSyncDelay())
            {
                return 0;
            }
            if (LocalMultiplayer.IsLocalMultiplayer(true))
            {
                return 4;
            }
            return this.defaultInterpolationTicks;
        }

        public virtual IEnumerable<NetFarmerRoot> farmerRoots()
        {
            if (Game1.serverHost != null)
            {
                yield return Game1.serverHost;
            }
            foreach (NetRoot<Farmer> farmerRoot in Game1.otherFarmers.Roots.Values)
            {
                if (Game1.serverHost == null || farmerRoot != Game1.serverHost)
                {
                    yield return farmerRoot as NetFarmerRoot;
                }
            }
            Dictionary<long, NetRoot<Farmer>>.ValueCollection.Enumerator enumerator = default(Dictionary<long, NetRoot<Farmer>>.ValueCollection.Enumerator);
            yield break;
            yield break;
        }

        public virtual NetFarmerRoot farmerRoot(long id)
        {
            if (Game1.serverHost != null && id == Game1.serverHost.Value.UniqueMultiplayerID)
            {
                return Game1.serverHost;
            }
            if (Game1.otherFarmers.ContainsKey(id))
            {
                return Game1.otherFarmers.Roots[id] as NetFarmerRoot;
            }
            return null;
        }

        public virtual void broadcastFarmerDeltas()
        {
            foreach (NetFarmerRoot farmerRoot in this.farmerRoots())
            {
                if (farmerRoot.Dirty && Game1.player.UniqueMultiplayerID == farmerRoot.Value.UniqueMultiplayerID)
                {
                    this.broadcastFarmerDelta(farmerRoot.Value, this.writeObjectDeltaBytes<Farmer>(farmerRoot));
                }
            }
            if (Game1.player.teamRoot.Dirty)
            {
                this.broadcastTeamDelta(this.writeObjectDeltaBytes<FarmerTeam>(Game1.player.teamRoot));
            }
        }

        protected virtual void broadcastTeamDelta(byte[] delta)
        {
            if (Game1.IsServer)
            {
                using (IEnumerator<Farmer> enumerator = Game1.otherFarmers.Values.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        Farmer farmer = enumerator.Current;
                        if (farmer != Game1.player)
                        {
                            Game1.server.sendMessage(farmer.UniqueMultiplayerID, 13, Game1.player, new object[]
                            {
                                delta
                            });
                        }
                    }
                    return;
                }
            }
            if (Game1.IsClient)
            {
                Game1.client.sendMessage(13, new object[]
                {
                    delta
                });
            }
        }

        protected virtual void broadcastFarmerDelta(Farmer farmer, byte[] delta)
        {
            foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
            {
                if (v.Value.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
                {
                    v.Value.queueMessage(0, farmer, new object[]
                    {
                        farmer.UniqueMultiplayerID,
                        delta
                    });
                }
            }
        }

        public void updateRoot<T>(T root) where T : INetRoot
        {
            foreach (long id in this.disconnectingFarmers)
            {
                root.Disconnect(id);
            }
            root.TickTree();
        }

        public virtual void updateRoots()
        {
            this.updateRoot<NetRoot<IWorldState>>(Game1.netWorldState);
            foreach (NetFarmerRoot farmerRoot in this.farmerRoots())
            {
                farmerRoot.Clock.InterpolationTicks = this.interpolationTicks();
                this.updateRoot<NetFarmerRoot>(farmerRoot);
            }
            Game1.player.teamRoot.Clock.InterpolationTicks = this.interpolationTicks();
            this.updateRoot<NetRoot<FarmerTeam>>(Game1.player.teamRoot);
            if (Game1.IsClient)
            {
                using (IEnumerator<GameLocation> enumerator2 = this.activeLocations().GetEnumerator())
                {
                    while (enumerator2.MoveNext())
                    {
                        GameLocation location = enumerator2.Current;
                        if (location.Root != null && location.Root.Value == location)
                        {
                            location.Root.Clock.InterpolationTicks = this.interpolationTicks();
                            this.updateRoot<NetRoot<GameLocation>>(location.Root);
                        }
                    }
                    return;
                }
            }
            foreach (GameLocation loc in Game1.locations)
            {
                if (loc.Root != null)
                {
                    loc.Root.Clock.InterpolationTicks = this.interpolationTicks();
                    this.updateRoot<NetRoot<GameLocation>>(loc.Root);
                }
            }
            foreach (MineShaft mine in MineShaft.activeMines)
            {
                if (mine.Root != null)
                {
                    mine.Root.Clock.InterpolationTicks = this.interpolationTicks();
                    this.updateRoot<NetRoot<GameLocation>>(mine.Root);
                }
            }
            foreach (VolcanoDungeon level in VolcanoDungeon.activeLevels)
            {
                if (level.Root != null)
                {
                    level.Root.Clock.InterpolationTicks = this.interpolationTicks();
                    this.updateRoot<NetRoot<GameLocation>>(level.Root);
                }
            }
        }

        public virtual void broadcastLocationDeltas()
        {
            if (Game1.IsClient)
            {
                using (IEnumerator<GameLocation> enumerator = this.activeLocations().GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        GameLocation location = enumerator.Current;
                        if (!(location.Root == null) && location.Root.Dirty)
                        {
                            this.broadcastLocationDelta(location);
                        }
                    }
                    return;
                }
            }
            foreach (GameLocation loc in Game1.locations)
            {
                if (loc.Root != null && loc.Root.Dirty)
                {
                    this.broadcastLocationDelta(loc);
                }
            }
            MineShaft.ForEach(delegate (MineShaft mine)
            {
                if (mine.Root != null && mine.Root.Dirty)
                {
                    this.broadcastLocationDelta(mine);
                }
            });
            VolcanoDungeon.ForEach(delegate (VolcanoDungeon level)
            {
                if (level.Root != null && level.Root.Dirty)
                {
                    this.broadcastLocationDelta(level);
                }
            });
        }

        public virtual void broadcastLocationDelta(GameLocation loc)
        {
            if (loc.Root == null || !loc.Root.Dirty)
            {
                return;
            }
            byte[] delta = this.writeObjectDeltaBytes<GameLocation>(loc.Root);
            this.broadcastLocationBytes(loc, 6, delta);
        }

        protected virtual void broadcastLocationBytes(GameLocation loc, byte messageType, byte[] bytes)
        {
            OutgoingMessage message = new OutgoingMessage(messageType, Game1.player, new object[]
            {
                loc.isStructure.Value,
                loc.isStructure ? loc.uniqueName.Value : loc.name.Value,
                bytes
            });
            this.broadcastLocationMessage(loc, message);
        }

        protected virtual void broadcastLocationMessage(GameLocation loc, OutgoingMessage message)
        {
            if (Game1.IsClient)
            {
                Game1.client.sendMessage(message);
                return;
            }
            Action<Farmer> tellFarmer = delegate (Farmer f)
            {
                if (f != Game1.player)
                {
                    Game1.server.sendMessage(f.UniqueMultiplayerID, message);
                }
            };
            if (this.isAlwaysActiveLocation(loc))
            {
                using (IEnumerator<Farmer> enumerator = Game1.otherFarmers.Values.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        Farmer farmer = enumerator.Current;
                        tellFarmer(farmer);
                    }
                    return;
                }
            }
            foreach (Farmer f3 in loc.farmers)
            {
                tellFarmer(f3);
            }
            if (loc is BuildableGameLocation)
            {
                foreach (Building building in (loc as BuildableGameLocation).buildings)
                {
                    if (building.indoors.Value != null)
                    {
                        foreach (Farmer f2 in building.indoors.Value.farmers)
                        {
                            tellFarmer(f2);
                        }
                    }
                }
            }
        }

        public virtual void broadcastSprites(GameLocation location, List<TemporaryAnimatedSprite> sprites)
        {
            this.broadcastSprites(location, sprites.ToArray());
        }

        public virtual void broadcastSprites(GameLocation location, params TemporaryAnimatedSprite[] sprites)
        {
            location.temporarySprites.AddRange(sprites);
            if (sprites.Length == 0 || !Game1.IsMultiplayer)
            {
                return;
            }
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = this.createWriter(stream))
                {
                    writer.Push("TemporaryAnimatedSprites");
                    writer.Write(sprites.Length);
                    for (int i = 0; i < sprites.Length; i++)
                    {
                        sprites[i].Write(writer, location);
                    }
                    writer.Pop();
                }
                this.broadcastLocationBytes(location, 7, stream.ToArray());
            }
        }

        public virtual void broadcastWorldStateDeltas()
        {
            if (!Game1.netWorldState.Dirty)
            {
                return;
            }
            byte[] delta = this.writeObjectDeltaBytes<IWorldState>(Game1.netWorldState);
            foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
            {
                if (v.Value != Game1.player)
                {
                    v.Value.queueMessage(12, Game1.player, new object[]
                    {
                        delta
                    });
                }
            }
        }

        public virtual void receiveWorldState(BinaryReader msg)
        {
            Game1.netWorldState.Clock.InterpolationTicks = 0;
            this.readObjectDelta<IWorldState>(msg, Game1.netWorldState);
            Game1.netWorldState.TickTree();
            int origTime = Game1.timeOfDay;
            Game1.netWorldState.Value.WriteToGame1();
            if (!Game1.IsServer && origTime != Game1.timeOfDay && Game1.currentLocation != null && Game1.newDaySync == null)
            {
                Game1.performTenMinuteClockUpdate();
            }
        }

        public virtual void requestCharacterWarp(NPC character, GameLocation targetLocation, Vector2 position)
        {
            if (!Game1.IsClient)
            {
                return;
            }
            GameLocation loc = character.currentLocation;
            if (loc == null)
            {
                throw new ArgumentException("In warpCharacter, the character's currentLocation must not be null");
            }
            Guid characterGuid = loc.characters.GuidOf(character);
            if (characterGuid == Guid.Empty)
            {
                throw new ArgumentException("In warpCharacter, the character must be in its currentLocation");
            }
            OutgoingMessage message = new OutgoingMessage(8, Game1.player, new object[]
            {
                loc.isStructure.Value,
                loc.isStructure ? loc.uniqueName.Value : loc.name.Value,
                characterGuid,
                targetLocation.isStructure.Value,
                targetLocation.isStructure ? targetLocation.uniqueName.Value : targetLocation.name.Value,
                position
            });
            Game1.serverHost.Value.queueMessage(message);
        }

        public virtual NetRoot<GameLocation> locationRoot(GameLocation location)
        {
            if (location.Root == null && Game1.IsMasterGame)
            {
                new NetRoot<GameLocation>().Set(location);
                location.Root.Clock.InterpolationTicks = this.interpolationTicks();
                location.Root.MarkClean();
            }
            return location.Root;
        }

        public virtual void sendPassoutRequest()
        {
            object[] message = new object[]
            {
                Game1.player.UniqueMultiplayerID
            };
            if (Game1.IsMasterGame)
            {
                this._receivePassoutRequest(Game1.player);
                return;
            }
            Game1.client.sendMessage(28, message);
        }

        public virtual void receivePassoutRequest(IncomingMessage msg)
        {
            if (Game1.IsServer)
            {
                Farmer farmer = Game1.getFarmer(msg.Reader.ReadInt64());
                if (farmer != null)
                {
                    this._receivePassoutRequest(farmer);
                }
            }
        }

        protected virtual void _receivePassoutRequest(Farmer farmer)
        {
            if (Game1.IsMasterGame)
            {
                if (farmer.lastSleepLocation.Value != null && Game1.isLocationAccessible(farmer.lastSleepLocation) && Game1.getLocationFromName(farmer.lastSleepLocation) != null && Game1.getLocationFromName(farmer.lastSleepLocation).GetLocationContext() == farmer.currentLocation.GetLocationContext() && BedFurniture.IsBedHere(Game1.getLocationFromName(farmer.lastSleepLocation), farmer.lastSleepPoint.Value.X, farmer.lastSleepPoint.Value.Y))
                {
                    if (Game1.IsServer && farmer != Game1.player)
                    {
                        object[] message = new object[]
                        {
                            farmer.lastSleepLocation.Value,
                            farmer.lastSleepPoint.X,
                            farmer.lastSleepPoint.Y,
                            true
                        };
                        Game1.server.sendMessage(farmer.UniqueMultiplayerID, 29, Game1.player, message.ToArray<object>());
                        return;
                    }
                    Farmer.performPassoutWarp(farmer, farmer.lastSleepLocation, farmer.lastSleepPoint, true);
                    return;
                }
                else
                {
                    string wakeup_location = Utility.getHomeOfFarmer(farmer).NameOrUniqueName;
                    Point wakeup_point = Utility.getHomeOfFarmer(farmer).GetPlayerBedSpot();
                    bool has_bed = Utility.getHomeOfFarmer(farmer).GetPlayerBed() != null;
                    if (farmer.currentLocation.GetLocationContext() == GameLocation.LocationContext.Island)
                    {
                        IslandWest island_west = Game1.getLocationFromName("IslandWest") as IslandWest;
                        if (island_west != null && island_west.farmhouseRestored.Value)
                        {
                            IslandFarmHouse island_farmhouse = Game1.getLocationFromName("IslandFarmHouse") as IslandFarmHouse;
                            if (island_farmhouse != null)
                            {
                                wakeup_location = island_farmhouse.NameOrUniqueName;
                                wakeup_point = new Point(14, 17);
                                has_bed = false;
                                foreach (Furniture furniture in island_farmhouse.furniture)
                                {
                                    if (furniture is BedFurniture && (furniture as BedFurniture).bedType != BedFurniture.BedType.Child)
                                    {
                                        wakeup_point = (furniture as BedFurniture).GetBedSpot();
                                        has_bed = true;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    if (Game1.IsServer && farmer != Game1.player)
                    {
                        object[] message2 = new object[]
                        {
                            wakeup_location,
                            wakeup_point.X,
                            wakeup_point.Y,
                            has_bed
                        };
                        Game1.server.sendMessage(farmer.UniqueMultiplayerID, 29, Game1.player, message2.ToArray<object>());
                        return;
                    }
                    Farmer.performPassoutWarp(farmer, wakeup_location, wakeup_point, has_bed);
                }
            }
        }

        public virtual void receivePassout(IncomingMessage msg)
        {
            if (msg.SourceFarmer == Game1.serverHost.Value)
            {
                string wakeup_location = msg.Reader.ReadString();
                Point wakeup_point = new Point(msg.Reader.ReadInt32(), msg.Reader.ReadInt32());
                bool has_bed = msg.Reader.ReadBoolean();
                Farmer.performPassoutWarp(Game1.player, wakeup_location, wakeup_point, has_bed);
            }
        }

        public virtual void broadcastEvent(Event evt, GameLocation location, Vector2 positionBeforeEvent, bool use_local_farmer = true)
        {
            if (evt.id == -1)
            {
                return;
            }
            object[] message = new object[]
            {
                evt.id,
                use_local_farmer,
                (int)positionBeforeEvent.X,
                (int)positionBeforeEvent.Y,
                location.isStructure ? 1 : 0,
                location.isStructure ? location.uniqueName.Value : location.Name
            };
            if (Game1.IsServer)
            {
                using (NetRootDictionary<long, Farmer>.Enumerator enumerator = Game1.otherFarmers.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        KeyValuePair<long, Farmer> v = enumerator.Current;
                        if (v.Value.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
                        {
                            Game1.server.sendMessage(v.Value.UniqueMultiplayerID, 4, Game1.player, message);
                        }
                    }
                    return;
                }
            }
            if (Game1.IsClient)
            {
                Game1.client.sendMessage(4, message);
            }
        }

        protected virtual void receiveRequestGrandpaReevaluation(IncomingMessage msg)
        {
            Farm farm = Game1.getFarm();
            if (farm != null)
            {
                farm.requestGrandpaReevaluation();
            }
        }

        protected virtual void receiveFarmerKilledMonster(IncomingMessage msg)
        {
            if (msg.SourceFarmer == Game1.serverHost.Value)
            {
                string which = msg.Reader.ReadString();
                if (which != null)
                {
                    Game1.stats.monsterKilled(which);
                }
            }
        }

        public virtual void broadcastRemoveLocationFromLookup(GameLocation location)
        {
            List<object> message = new List<object>();
            message.Add(location.NameOrUniqueName);
            if (Game1.IsServer)
            {
                using (NetRootDictionary<long, Farmer>.Enumerator enumerator = Game1.otherFarmers.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        KeyValuePair<long, Farmer> v = enumerator.Current;
                        if (v.Value.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
                        {
                            Game1.server.sendMessage(v.Value.UniqueMultiplayerID, 24, Game1.player, message.ToArray());
                        }
                    }
                    return;
                }
            }
            if (Game1.IsClient)
            {
                Game1.client.sendMessage(24, message.ToArray());
            }
        }

        public virtual void broadcastNutDig(GameLocation location, Point point)
        {
            if (Game1.IsMasterGame)
            {
                this._performNutDig(location, point);
                return;
            }
            List<object> message = new List<object>();
            message.Add(location.NameOrUniqueName);
            message.Add(point.X);
            message.Add(point.Y);
            Game1.client.sendMessage(27, message.ToArray());
        }

        protected virtual void receiveNutDig(IncomingMessage msg)
        {
            if (!Game1.IsMasterGame)
            {
                return;
            }
            string name = msg.Reader.ReadString();
            Point point = new Point(msg.Reader.ReadInt32(), msg.Reader.ReadInt32());
            GameLocation location = Game1.getLocationFromName(name);
            this._performNutDig(location, point);
        }

        protected virtual void _performNutDig(GameLocation location, Point point)
        {
            if (location is IslandLocation)
            {
                IslandLocation island_location = location as IslandLocation;
                if (island_location.IsBuriedNutLocation(point))
                {
                    string key = string.Concat(new string[]
                    {
                        location.NameOrUniqueName,
                        "_",
                        point.X.ToString(),
                        "_",
                        point.Y.ToString()
                    });
                    if (!Game1.netWorldState.Value.FoundBuriedNuts.ContainsKey(key))
                    {
                        Game1.netWorldState.Value.FoundBuriedNuts[key] = true;
                        Game1.createItemDebris(new StardewValley.Object(73, 1, false, -1, 0), new Vector2((float)point.X, (float)point.Y) * 64f, -1, island_location, -1);
                    }
                }
            }
        }

        public virtual void broadcastPartyWideMail(string mail_key, Multiplayer.PartyWideMessageQueue message_queue = Multiplayer.PartyWideMessageQueue.MailForTomorrow, bool no_letter = false)
        {
            mail_key = mail_key.Trim();
            mail_key = mail_key.Replace(Environment.NewLine, "");
            List<object> message = new List<object>();
            message.Add(mail_key);
            message.Add((int)message_queue);
            message.Add(no_letter);
            this._performPartyWideMail(mail_key, message_queue, no_letter);
            if (Game1.IsServer)
            {
                using (NetRootDictionary<long, Farmer>.Enumerator enumerator = Game1.otherFarmers.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        KeyValuePair<long, Farmer> v = enumerator.Current;
                        if (v.Value.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
                        {
                            Game1.server.sendMessage(v.Value.UniqueMultiplayerID, 22, Game1.player, message.ToArray());
                        }
                    }
                    return;
                }
            }
            if (Game1.IsClient)
            {
                Game1.client.sendMessage(22, message.ToArray());
            }
        }

        public virtual void broadcastGrandpaReevaluation()
        {
            Game1.getFarm().requestGrandpaReevaluation();
            if (Game1.IsServer)
            {
                using (NetRootDictionary<long, Farmer>.Enumerator enumerator = Game1.otherFarmers.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        KeyValuePair<long, Farmer> v = enumerator.Current;
                        if (v.Value.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
                        {
                            Game1.server.sendMessage(v.Value.UniqueMultiplayerID, 26, Game1.player, Array.Empty<object>());
                        }
                    }
                    return;
                }
            }
            if (Game1.IsClient)
            {
                Game1.client.sendMessage(26, Array.Empty<object>());
            }
        }

        public virtual void broadcastGlobalMessage(string localization_string_key, bool only_show_if_empty = false, params string[] substitutions)
        {
            if (!only_show_if_empty || Game1.hudMessages.Count == 0)
            {
                Game1.showGlobalMessage(Game1.content.LoadString(localization_string_key, substitutions));
            }
            List<object> message = new List<object>();
            message.Add(localization_string_key);
            message.Add(only_show_if_empty);
            message.Add(substitutions.Length);
            for (int i = 0; i < substitutions.Length; i++)
            {
                message.Add(substitutions[i]);
            }
            if (Game1.IsServer)
            {
                using (NetRootDictionary<long, Farmer>.Enumerator enumerator = Game1.otherFarmers.GetEnumerator())
                {
                    while (enumerator.MoveNext())
                    {
                        KeyValuePair<long, Farmer> v = enumerator.Current;
                        if (v.Value.UniqueMultiplayerID != Game1.player.UniqueMultiplayerID)
                        {
                            Game1.server.sendMessage(v.Value.UniqueMultiplayerID, 21, Game1.player, message.ToArray());
                        }
                    }
                    return;
                }
            }
            if (Game1.IsClient)
            {
                Game1.client.sendMessage(21, message.ToArray());
            }
        }

        public virtual NetRoot<T> readObjectFull<T>(BinaryReader reader) where T : class, INetObject<INetSerializable>
        {
            NetRoot<T> netRoot = NetRoot<T>.Connect(reader);
            netRoot.Clock.InterpolationTicks = this.defaultInterpolationTicks;
            return netRoot;
        }

        protected virtual BinaryWriter createWriter(Stream stream)
        {
            BinaryWriter writer = new BinaryWriter(stream);
            if (this.logging.IsLogging)
            {
                writer = new LoggingBinaryWriter(writer);
            }
            return writer;
        }

        public virtual void writeObjectFull<T>(BinaryWriter writer, NetRoot<T> root, long? peer) where T : class, INetObject<INetSerializable>
        {
            root.CreateConnectionPacket(writer, peer);
        }

        public virtual byte[] writeObjectFullBytes<T>(NetRoot<T> root, long? peer) where T : class, INetObject<INetSerializable>
        {
            byte[] result;
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = this.createWriter(stream))
                {
                    root.CreateConnectionPacket(writer, peer);
                    result = stream.ToArray();
                }
            }
            return result;
        }

        public virtual void readObjectDelta<T>(BinaryReader reader, NetRoot<T> root) where T : class, INetObject<INetSerializable>
        {
            root.Read(reader);
        }

        public virtual void writeObjectDelta<T>(BinaryWriter writer, NetRoot<T> root) where T : class, INetObject<INetSerializable>
        {
            root.Write(writer);
        }

        public virtual byte[] writeObjectDeltaBytes<T>(NetRoot<T> root) where T : class, INetObject<INetSerializable>
        {
            byte[] result;
            using (MemoryStream stream = new MemoryStream())
            {
                using (BinaryWriter writer = this.createWriter(stream))
                {
                    root.Write(writer);
                    result = stream.ToArray();
                }
            }
            return result;
        }

        public virtual NetFarmerRoot readFarmer(BinaryReader reader)
        {
            NetFarmerRoot netFarmerRoot = new NetFarmerRoot();
            netFarmerRoot.ReadConnectionPacket(reader);
            netFarmerRoot.Clock.InterpolationTicks = this.defaultInterpolationTicks;
            return netFarmerRoot;
        }

        public virtual void addPlayer(NetFarmerRoot f)
        {
            long id = f.Value.UniqueMultiplayerID;
            f.Value.teamRoot = Game1.player.teamRoot;
            Game1.otherFarmers.Roots[id] = f;
            this.disconnectingFarmers.Remove(id);
            if (Game1.chatBox != null)
            {
                string farmerName = ChatBox.formattedUserNameLong(f.Value);
                Game1.chatBox.addInfoMessage(Game1.content.LoadString("Strings\\UI:Chat_PlayerJoined", farmerName));
            }
        }

        public virtual void receivePlayerIntroduction(BinaryReader reader)
        {
            this.addPlayer(this.readFarmer(reader));
        }

        public virtual void broadcastPlayerIntroduction(NetFarmerRoot farmerRoot)
        {
            if (Game1.server == null)
            {
                return;
            }
            foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
            {
                if (farmerRoot.Value.UniqueMultiplayerID != v.Value.UniqueMultiplayerID)
                {
                    Game1.server.sendMessage(v.Value.UniqueMultiplayerID, 2, farmerRoot.Value, new object[]
                    {
                        Game1.server.getUserName(farmerRoot.Value.UniqueMultiplayerID),
                        this.writeObjectFullBytes<Farmer>(farmerRoot, new long?(v.Value.UniqueMultiplayerID))
                    });
                }
            }
        }

        public virtual void broadcastUserName(long farmerId, string userName)
        {
            if (Game1.server != null)
            {
                return;
            }
            foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
            {
                Farmer farmer = v.Value;
                if (farmer.UniqueMultiplayerID != farmerId)
                {
                    Game1.server.sendMessage(farmer.UniqueMultiplayerID, 16, Game1.serverHost.Value, new object[]
                    {
                        farmerId,
                        userName
                    });
                }
            }
        }

        public virtual string getUserName(long id)
        {
            if (id == Game1.player.UniqueMultiplayerID)
            {
                return Game1.content.LoadString("Strings\\UI:Chat_SelfPlayerID");
            }
            if (Game1.server != null)
            {
                return Game1.server.getUserName(id);
            }
            if (Game1.client != null)
            {
                return Game1.client.getUserName(id);
            }
            return "?";
        }

        public virtual void playerDisconnected(long id)
        {
            if (Game1.otherFarmers.ContainsKey(id) && !this.disconnectingFarmers.Contains(id))
            {
                NetFarmerRoot farmhand = Game1.otherFarmers.Roots[id] as NetFarmerRoot;
                if (farmhand.Value.mount != null && Game1.IsMasterGame)
                {
                    farmhand.Value.mount.dismount(false);
                }
                if (Game1.IsMasterGame)
                {
                    this.saveFarmhand(farmhand);
                    farmhand.Value.handleDisconnect();
                }
                if (Game1.player.dancePartner.Value is Farmer && ((Farmer)Game1.player.dancePartner.Value).UniqueMultiplayerID == farmhand.Value.UniqueMultiplayerID)
                {
                    Game1.player.dancePartner.Value = null;
                }
                if (Game1.chatBox != null)
                {
                    Game1.chatBox.addInfoMessage(Game1.content.LoadString("Strings\\UI:Chat_PlayerLeft", ChatBox.formattedUserNameLong(Game1.otherFarmers[id])));
                }
                this.disconnectingFarmers.Add(id);
            }
        }

        protected virtual void removeDisconnectedFarmers()
        {
            foreach (long id in this.disconnectingFarmers)
            {
                Game1.otherFarmers.Remove(id);
            }
            this.disconnectingFarmers.Clear();
        }

        public virtual void sendFarmhand()
        {
            (Game1.player.NetFields.Root as NetFarmerRoot).MarkReassigned();
        }

        protected virtual void saveFarmhand(NetFarmerRoot farmhand)
        {
            FarmHouse farmHouse = Utility.getHomeOfFarmer(farmhand);
            if (farmHouse is Cabin)
            {
                (farmHouse as Cabin).saveFarmhand(farmhand);
            }
        }

        public virtual void saveFarmhands()
        {
            if (!Game1.IsMasterGame)
            {
                return;
            }
            foreach (NetRoot<Farmer> farmer in Game1.otherFarmers.Roots.Values)
            {
                this.saveFarmhand(farmer as NetFarmerRoot);
            }
        }

        public virtual void clientRemotelyDisconnected(Multiplayer.DisconnectType disconnectType)
        {
            Multiplayer.LogDisconnect(disconnectType);
            this.returnToMainMenu();
        }

        private void returnToMainMenu()
        {
            if (!Game1.game1.IsMainInstance)
            {
                GameRunner.instance.RemoveGameInstance(Game1.game1);
                return;
            }
            Game1.ExitToTitle(delegate
            {
                (Game1.activeClickableMenu as TitleMenu).skipToTitleButtons();
                TitleMenu.subMenu = new ConfirmationDialog(Game1.content.LoadString("Strings\\UI:Client_RemotelyDisconnected"), null, null)
                {
                    okButton =
                {
                        visible = false
                }
                };
            });
        }

        public static bool ShouldLogDisconnect(Multiplayer.DisconnectType disconnectType)
        {
            switch (disconnectType)
            {
                case Multiplayer.DisconnectType.ClosedGame:
                case Multiplayer.DisconnectType.ExitedToMainMenu:
                case Multiplayer.DisconnectType.ExitedToMainMenu_FromFarmhandSelect:
                case Multiplayer.DisconnectType.ServerOfflineMode:
                case Multiplayer.DisconnectType.ServerFull:
                case Multiplayer.DisconnectType.AcceptedOtherInvite:
                    return false;
            }
            return true;
        }

        public static bool IsTimeout(Multiplayer.DisconnectType disconnectType)
        {
            return disconnectType - Multiplayer.DisconnectType.ClientTimeout <= 2;
        }

        public static void LogDisconnect(Multiplayer.DisconnectType disconnectType)
        {
            if (Multiplayer.ShouldLogDisconnect(disconnectType))
            {
                string message = "Disconnected at : " + DateTime.Now.ToLongTimeString() + " - " + disconnectType.ToString();
                if (Game1.client != null)
                {
                    message = message + " Ping: " + Game1.client.GetPingToHost().ToString("0.#");
                    message += ((Game1.client is LidgrenClient) ? " ip" : " friend/invite");
                }
                Program.WriteLog(Program.LogType.Disconnect, message, true);
            }
            Console.WriteLine("Disconnected: " + disconnectType.ToString());
        }

        public virtual void sendSharedAchievementMessage(int achievement)
        {
            if (Game1.IsClient)
            {
                Game1.client.sendMessage(20, new object[]
                {
                    achievement
                });
                return;
            }
            if (Game1.IsServer)
            {
                foreach (long id in Game1.otherFarmers.Keys)
                {
                    Game1.server.sendMessage(id, 20, Game1.player, new object[]
                    {
                        achievement
                    });
                }
            }
        }

        public virtual void sendServerToClientsMessage(string message)
        {
            if (Game1.IsServer)
            {
                foreach (KeyValuePair<long, Farmer> v in Game1.otherFarmers)
                {
                    v.Value.queueMessage(18, Game1.player, new object[]
                    {
                        message
                    });
                }
            }
        }

        public virtual void sendChatMessage(LocalizedContentManager.LanguageCode language, string message, long recipientID)
        {
            if (Game1.IsClient)
            {
                Game1.client.sendMessage(10, new object[]
                {
                    recipientID,
                    language,
                    message
                });
                return;
            }
            if (Game1.IsServer)
            {
                if (recipientID == Multiplayer.AllPlayers)
                {
                    using (IEnumerator<long> enumerator = Game1.otherFarmers.Keys.GetEnumerator())
                    {
                        while (enumerator.MoveNext())
                        {
                            long id = enumerator.Current;
                            Game1.server.sendMessage(id, 10, Game1.player, new object[]
                            {
                                recipientID,
                                language,
                                message
                            });
                        }
                        return;
                    }
                }
                Game1.server.sendMessage(recipientID, 10, Game1.player, new object[]
                {
                    recipientID,
                    language,
                    message
                });
            }
        }

        public virtual void receiveChatMessage(Farmer sourceFarmer, long recipientID, LocalizedContentManager.LanguageCode language, string message)
        {
            if (Game1.chatBox != null)
            {
                int messageType = 0;
                message = ProgramPatch.sdk.FilterDirtyWords(message);
                if (recipientID != Multiplayer.AllPlayers)
                {
                    messageType = 3;
                }
                Game1.chatBox.receiveChatMessage(sourceFarmer.UniqueMultiplayerID, messageType, language, message);
            }
        }

        public virtual void globalChatInfoMessage(string messageKey, params string[] args)
        {
            if (!Game1.IsMultiplayer && Game1.multiplayerMode == 0)
            {
                return;
            }
            this.receiveChatInfoMessage(Game1.player, messageKey, args);
            this.sendChatInfoMessage(messageKey, args);
        }

        public void globalChatInfoMessageEvenInSinglePlayer(string messageKey, params string[] args)
        {
            this.receiveChatInfoMessage(Game1.player, messageKey, args);
            this.sendChatInfoMessage(messageKey, args);
        }

        protected virtual void sendChatInfoMessage(string messageKey, params string[] args)
        {
            if (Game1.IsClient)
            {
                Game1.client.sendMessage(15, new object[]
                {
                    messageKey,
                    args
                });
                return;
            }
            if (Game1.IsServer)
            {
                foreach (long id in Game1.otherFarmers.Keys)
                {
                    Game1.server.sendMessage(id, 15, Game1.player, new object[]
                    {
                        messageKey,
                        args
                    });
                }
            }
        }

        protected virtual void receiveChatInfoMessage(Farmer sourceFarmer, string messageKey, string[] args)
        {
            if (Game1.chatBox != null)
            {
                try
                {
                    string[] processedArgs = args.Select(delegate (string arg)
                    {
                        if (arg.StartsWith("achievement:"))
                        {
                            int index = Convert.ToInt32(arg.Substring("achievement:".Length));
                            return Game1.content.Load<Dictionary<int, string>>("Data\\Achievements")[index].Split('^', StringSplitOptions.None)[0];
                        }
                        if (arg.StartsWith("object:"))
                        {
                            return new StardewValley.Object(Convert.ToInt32(arg.Substring("object:".Length)), 1, false, -1, 0).DisplayName;
                        }
                        return arg;
                    }).ToArray<string>();
                    ChatBox chatBox = Game1.chatBox;
                    LocalizedContentManager content = Game1.content;
                    string path = "Strings\\UI:Chat_" + messageKey;
                    object[] substitutions = processedArgs;
                    chatBox.addInfoMessage(content.LoadString(path, substitutions));
                }
                catch (ContentLoadException)
                {
                }
                catch (FormatException)
                {
                }
                catch (OverflowException)
                {
                }
                catch (KeyNotFoundException)
                {
                }
            }
        }

        public virtual void parseServerToClientsMessage(string message)
        {
            if (Game1.IsClient)
            {
                if (!(message == "festivalEvent"))
                {
                    if (!(message == "endFest"))
                    {
                        if (!(message == "trainApproach"))
                        {
                            return;
                        }
                        GameLocation railroad = Game1.getLocationFromName("Railroad");
                        if (railroad != null && railroad is Railroad)
                        {
                            ((Railroad)railroad).PlayTrainApproach();
                        }
                    }
                    else if (Game1.CurrentEvent != null)
                    {
                        Game1.CurrentEvent.forceEndFestival(Game1.player);
                        return;
                    }
                }
                else if (Game1.currentLocation.currentEvent != null)
                {
                    Game1.currentLocation.currentEvent.forceFestivalContinue();
                    return;
                }
            }
        }

        public virtual IEnumerable<GameLocation> activeLocations()
        {
            if (Game1.currentLocation != null)
            {
                yield return Game1.currentLocation;
            }
            Farm farm = Game1.getFarm();
            if (farm != null && farm != Game1.currentLocation)
            {
                yield return farm;
            }
            GameLocation farmhouse = Game1.getLocationFromName("FarmHouse");
            if (farmhouse != null && farmhouse != Game1.currentLocation)
            {
                yield return farmhouse;
            }
            GameLocation greenhouse = Game1.getLocationFromName("Greenhouse");
            if (greenhouse != null && greenhouse != Game1.currentLocation)
            {
                yield return greenhouse;
            }
            foreach (Building building in farm.buildings)
            {
                if (building.indoors.Value != null && building.indoors.Value != Game1.currentLocation)
                {
                    yield return building.indoors.Value;
                }
            }
            List<Building>.Enumerator enumerator = default(List<Building>.Enumerator);
            yield break;
            yield break;
        }

        public virtual bool isAlwaysActiveLocation(GameLocation location)
        {
            return location.Name == "Farm" || location.Name == "FarmHouse" || location.Name == "Greenhouse" || (location.Root != null && location.Root.Value.Equals(Game1.getFarm()));
        }

        protected virtual void readActiveLocation(IncomingMessage msg)
        {
            bool force_current_location = msg.Reader.ReadBoolean();
            NetRoot<GameLocation> root = this.readObjectFull<GameLocation>(msg.Reader);
            if (this.isAlwaysActiveLocation(root.Value))
            {
                int i = 0;
                while (i < Game1.locations.Count)
                {
                    if (Game1.locations[i].Equals(root.Value))
                    {
                        if (Game1.locations[i] != root.Value)
                        {
                            if (Game1.locations[i] != null)
                            {
                                if (Game1.currentLocation == Game1.locations[i])
                                {
                                    Game1.currentLocation = root.Value;
                                }
                                if (Game1.player.currentLocation == Game1.locations[i])
                                {
                                    Game1.player.currentLocation = root.Value;
                                }
                                Game1.removeLocationFromLocationLookup(Game1.locations[i]);
                            }
                            Game1.locations[i] = root.Value;
                            break;
                        }
                        break;
                    }
                    else
                    {
                        i++;
                    }
                }
            }
            if (Game1.locationRequest != null || force_current_location)
            {
                if (Game1.locationRequest != null)
                {
                    Game1.currentLocation = Game1.findStructure(root.Value, Game1.locationRequest.Name);
                    if (Game1.currentLocation == null)
                    {
                        Game1.currentLocation = root.Value;
                    }
                }
                else if (force_current_location)
                {
                    Game1.currentLocation = root.Value;
                }
                if (Game1.locationRequest != null)
                {
                    Game1.locationRequest.Location = root.Value;
                    Game1.locationRequest.Loaded(root.Value);
                }
                Game1.currentLocation.resetForPlayerEntry();
                Game1.player.currentLocation = Game1.currentLocation;
                if (Game1.locationRequest != null)
                {
                    Game1.locationRequest.Warped(root.Value);
                }
                Game1.currentLocation.updateSeasonalTileSheets(null);
                if (Game1.IsDebrisWeatherHere(null))
                {
                    Game1.populateDebrisWeatherArray();
                }
                Game1.locationRequest = null;
            }
        }

        public virtual bool isActiveLocation(GameLocation location)
        {
            return Game1.IsMasterGame || (Game1.currentLocation != null && Game1.currentLocation.Root != null && Game1.currentLocation.Root.Value == location.Root.Value) || this.isAlwaysActiveLocation(location);
        }

        protected virtual GameLocation readLocation(BinaryReader reader)
        {
            bool structure = reader.ReadByte() > 0;
            GameLocation location = Game1.getLocationFromName(reader.ReadString(), structure);
            if (location == null || this.locationRoot(location) == null)
            {
                return null;
            }
            if (!this.isActiveLocation(location))
            {
                return null;
            }
            return location;
        }

        protected virtual LocationRequest readLocationRequest(BinaryReader reader)
        {
            bool structure = reader.ReadByte() > 0;
            return Game1.getLocationRequest(reader.ReadString(), structure);
        }

        protected virtual void readWarp(BinaryReader reader, int tileX, int tileY, Action afterWarp)
        {
            LocationRequest request = this.readLocationRequest(reader);
            if (afterWarp != null)
            {
                request.OnWarp += afterWarp.Invoke;
            }
            Game1.warpFarmer(request, tileX, tileY, Game1.player.FacingDirection);
        }

        protected virtual NPC readNPC(BinaryReader reader)
        {
            GameLocation location = this.readLocation(reader);
            Guid guid = reader.ReadGuid();
            if (!location.characters.ContainsGuid(guid))
            {
                return null;
            }
            return location.characters[guid];
        }

        public virtual TemporaryAnimatedSprite[] readSprites(BinaryReader reader, GameLocation location)
        {
            int count = reader.ReadInt32();
            TemporaryAnimatedSprite[] result = new TemporaryAnimatedSprite[count];
            for (int i = 0; i < count; i++)
            {
                TemporaryAnimatedSprite sprite = new TemporaryAnimatedSprite();
                sprite.Read(reader, location);
                sprite.ticksBeforeAnimationStart += this.interpolationTicks();
                result[i] = sprite;
            }
            return result;
        }

        protected virtual void receiveTeamDelta(BinaryReader msg)
        {
            this.readObjectDelta<FarmerTeam>(msg, Game1.player.teamRoot);
        }

        protected virtual void receiveNewDaySync(IncomingMessage msg)
        {
            if (Game1.newDaySync == null && msg.SourceFarmer == Game1.serverHost.Value)
            {
                Game1.NewDay(0f);
            }
            if (Game1.newDaySync != null)
            {
                Game1.newDaySync.receiveMessage(msg);
            }
        }

        protected virtual void receiveFarmerGainExperience(IncomingMessage msg)
        {
            if (msg.SourceFarmer == Game1.serverHost.Value)
            {
                int which = msg.Reader.ReadInt32();
                int howMuch = msg.Reader.ReadInt32();
                Game1.player.gainExperience(which, howMuch);
            }
        }

        protected virtual void receiveSharedAchievement(IncomingMessage msg)
        {
            Game1.getAchievement(msg.Reader.ReadInt32(), false);
        }

        protected virtual void receiveRemoveLocationFromLookup(IncomingMessage msg)
        {
            Game1.removeLocationFromLocationLookup(msg.Reader.ReadString());
        }

        protected virtual void receivePartyWideMail(IncomingMessage msg)
        {
            string mail_key = msg.Reader.ReadString();
            Multiplayer.PartyWideMessageQueue message_queue = (Multiplayer.PartyWideMessageQueue)msg.Reader.ReadInt32();
            bool no_letter = msg.Reader.ReadBoolean();
            this._performPartyWideMail(mail_key, message_queue, no_letter);
        }

        protected void _performPartyWideMail(string mail_key, Multiplayer.PartyWideMessageQueue message_queue, bool no_letter)
        {
            if (message_queue == Multiplayer.PartyWideMessageQueue.MailForTomorrow)
            {
                Game1.addMailForTomorrow(mail_key, no_letter, false);
            }
            else if (message_queue == Multiplayer.PartyWideMessageQueue.SeenMail)
            {
                Game1.addMail(mail_key, no_letter, false);
            }
            if (no_letter)
            {
                mail_key += "%&NL&%";
            }
            if (message_queue == Multiplayer.PartyWideMessageQueue.MailForTomorrow)
            {
                mail_key = "%&MFT&%" + mail_key;
            }
            else if (message_queue == Multiplayer.PartyWideMessageQueue.SeenMail)
            {
                mail_key = "%&SM&%" + mail_key;
            }
            if (Game1.IsMasterGame && !Game1.player.team.broadcastedMail.Contains(mail_key))
            {
                Game1.player.team.broadcastedMail.Add(mail_key);
            }
        }

        protected void receiveForceKick()
        {
            if (Game1.IsServer)
            {
                return;
            }
            this.Disconnect(Multiplayer.DisconnectType.Kicked);
            this.returnToMainMenu();
        }

        protected virtual void receiveGlobalMessage(IncomingMessage msg)
        {
            string localization_string_key = msg.Reader.ReadString();
            if (msg.Reader.ReadBoolean() && Game1.hudMessages.Count > 0)
            {
                return;
            }
            int count = msg.Reader.ReadInt32();
            object[] substitutions = new object[count];
            for (int i = 0; i < count; i++)
            {
                substitutions[i] = msg.Reader.ReadString();
            }
            Game1.showGlobalMessage(Game1.content.LoadString(localization_string_key, substitutions));
        }

        public virtual void processIncomingMessage(IncomingMessage msg)
        {
            switch (msg.MessageType)
            {
                case 0:
                    {
                        long f = msg.Reader.ReadInt64();
                        NetFarmerRoot farmer = this.farmerRoot(f);
                        if (farmer != null)
                        {
                            this.readObjectDelta<Farmer>(msg.Reader, farmer);
                            return;
                        }
                        break;
                    }
                case 1:
                case 5:
                case 9:
                case 11:
                case 16:
                    break;
                case 2:
                    this.receivePlayerIntroduction(msg.Reader);
                    return;
                case 3:
                    this.readActiveLocation(msg);
                    return;
                case 4:
                    {
                        int eventId = msg.Reader.ReadInt32();
                        bool use_local_farmer = msg.Reader.ReadBoolean();
                        int tileX = msg.Reader.ReadInt32();
                        int tileY = msg.Reader.ReadInt32();
                        LocationRequest request = this.readLocationRequest(msg.Reader);
                        GameLocation location_for_event_check = Game1.getLocationFromName(request.Name);
                        if (location_for_event_check == null || location_for_event_check.findEventById(eventId, null) == null)
                        {
                            Console.WriteLine("Couldn't find event " + eventId.ToString() + " for broadcast event!");
                            return;
                        }
                        Farmer farmerActor = null;
                        if (use_local_farmer)
                        {
                            farmerActor = (Game1.player.NetFields.Root as NetRoot<Farmer>).Clone().Value;
                        }
                        else
                        {
                            farmerActor = (msg.SourceFarmer.NetFields.Root as NetRoot<Farmer>).Clone().Value;
                        }
                        int old_x = (int)Game1.player.getTileLocation().X;
                        int old_y = (int)Game1.player.getTileLocation().Y;
                        string old_location = Game1.player.currentLocation.NameOrUniqueName;
                        int direction = Game1.player.facingDirection.Value;
                        Game1.player.locationBeforeForcedEvent.Value = old_location;
                        request.OnWarp += delegate ()
                        {
                            farmerActor.currentLocation = Game1.currentLocation;
                            farmerActor.completelyStopAnimatingOrDoingAction();
                            farmerActor.UsingTool = false;
                            farmerActor.items.Clear();
                            farmerActor.hidden.Value = false;
                            Event evt = Game1.currentLocation.findEventById(eventId, farmerActor);
                            Game1.currentLocation.startEvent(evt);
                            farmerActor.Position = Game1.player.Position;
                            Game1.warpingForForcedRemoteEvent = false;
                            string old_location_before_event = Game1.player.locationBeforeForcedEvent.Value;
                            Game1.player.locationBeforeForcedEvent.Value = null;
                            evt.setExitLocation(old_location, old_x, old_y);
                            Game1.player.locationBeforeForcedEvent.Value = old_location_before_event;
                            Game1.player.orientationBeforeEvent = direction;
                        };
                        Action performForcedEvent = delegate ()
                        {
                            Game1.warpingForForcedRemoteEvent = true;
                            Game1.player.completelyStopAnimatingOrDoingAction();
                            Game1.warpFarmer(request, tileX, tileY, Game1.player.FacingDirection);
                        };
                        Game1.remoteEventQueue.Add(performForcedEvent);
                        return;
                    }
                case 6:
                    {
                        GameLocation location = this.readLocation(msg.Reader);
                        if (location != null)
                        {
                            this.readObjectDelta<GameLocation>(msg.Reader, location.Root);
                            return;
                        }
                        break;
                    }
                case 7:
                    {
                        GameLocation location = this.readLocation(msg.Reader);
                        if (location != null)
                        {
                            location.temporarySprites.AddRange(this.readSprites(msg.Reader, location));
                            return;
                        }
                        break;
                    }
                case 8:
                    {
                        NPC character = this.readNPC(msg.Reader);
                        GameLocation location = this.readLocation(msg.Reader);
                        if (character != null && location != null)
                        {
                            Game1.warpCharacter(character, location, msg.Reader.ReadVector2());
                            return;
                        }
                        break;
                    }
                case 10:
                    {
                        long recipientId = msg.Reader.ReadInt64();
                        LocalizedContentManager.LanguageCode langCode = msg.Reader.ReadEnum<LocalizedContentManager.LanguageCode>();
                        string message = msg.Reader.ReadString();
                        this.receiveChatMessage(msg.SourceFarmer, recipientId, langCode, message);
                        return;
                    }
                case 12:
                    this.receiveWorldState(msg.Reader);
                    return;
                case 13:
                    this.receiveTeamDelta(msg.Reader);
                    return;
                case 14:
                    this.receiveNewDaySync(msg);
                    return;
                case 15:
                    {
                        string messageKey = msg.Reader.ReadString();
                        string[] args = new string[(int)msg.Reader.ReadByte()];
                        for (int i = 0; i < args.Length; i++)
                        {
                            args[i] = msg.Reader.ReadString();
                        }
                        this.receiveChatInfoMessage(msg.SourceFarmer, messageKey, args);
                        return;
                    }
                case 17:
                    this.receiveFarmerGainExperience(msg);
                    return;
                case 18:
                    this.parseServerToClientsMessage(msg.Reader.ReadString());
                    return;
                case 19:
                    this.playerDisconnected(msg.SourceFarmer.UniqueMultiplayerID);
                    return;
                case 20:
                    this.receiveSharedAchievement(msg);
                    return;
                case 21:
                    this.receiveGlobalMessage(msg);
                    return;
                case 22:
                    this.receivePartyWideMail(msg);
                    return;
                case 23:
                    this.receiveForceKick();
                    return;
                case 24:
                    this.receiveRemoveLocationFromLookup(msg);
                    return;
                case 25:
                    this.receiveFarmerKilledMonster(msg);
                    return;
                case 26:
                    this.receiveRequestGrandpaReevaluation(msg);
                    return;
                case 27:
                    this.receiveNutDig(msg);
                    return;
                case 28:
                    this.receivePassoutRequest(msg);
                    return;
                case 29:
                    this.receivePassout(msg);
                    break;
                default:
                    return;
            }
        }

        public virtual void StartLocalMultiplayerServer()
        {
            Game1.server = new GameServer(true);
            Game1.server.startServer();
        }

        public virtual void StartServer()
        {
            Game1.server = new GameServer(false);
            Game1.server.startServer();
        }

        public virtual void Disconnect(Multiplayer.DisconnectType disconnectType)
        {
            if (Game1.server != null)
            {
                Game1.server.stopServer();
                Game1.server = null;
                foreach (long id in Game1.otherFarmers.Keys)
                {
                    this.playerDisconnected(id);
                }
            }
            if (Game1.client != null)
            {
                this.sendFarmhand();
                this.UpdateLate(true);
                Game1.client.disconnect(true);
                Game1.client = null;
            }
            Game1.otherFarmers.Clear();
            Multiplayer.LogDisconnect(disconnectType);
        }

        protected virtual void updatePendingConnections()
        {
            if (Game1.multiplayerMode == 2)
            {
                if (Game1.server == null && Game1.options.enableServer)
                {
                    this.StartServer();
                    return;
                }
            }
            else if (Game1.multiplayerMode == 1)
            {
                if (Game1.client == null || Game1.client.readyToPlay)
                {
                    return;
                }
                Game1.client.receiveMessages();
            }
        }

        public void UpdateLoading()
        {
            this.updatePendingConnections();
            if (Game1.server != null)
            {
                Game1.server.receiveMessages();
            }
        }

        public virtual void UpdateEarly()
        {
            this.updatePendingConnections();
            if (Game1.multiplayerMode == 2 && Game1.serverHost == null && Game1.options.enableServer)
            {
                Game1.server.initializeHost();
            }
            if (Game1.server != null)
            {
                Game1.server.receiveMessages();
            }
            else if (Game1.client != null)
            {
                Game1.client.receiveMessages();
            }
            this.updateRoots();
            if (Game1.CurrentEvent == null)
            {
                this.removeDisconnectedFarmers();
            }
        }

        public virtual void UpdateLate(bool forceSync = false)
        {
            if (Game1.multiplayerMode != 0)
            {
                if (!this.allowSyncDelay() || forceSync || Game1.ticks % this.farmerDeltaBroadcastPeriod == 0)
                {
                    this.broadcastFarmerDeltas();
                }
                if (!this.allowSyncDelay() || forceSync || Game1.ticks % this.locationDeltaBroadcastPeriod == 0)
                {
                    this.broadcastLocationDeltas();
                }
                if (!this.allowSyncDelay() || forceSync || Game1.ticks % this.worldStateDeltaBroadcastPeriod == 0)
                {
                    this.broadcastWorldStateDeltas();
                }
            }
            if (Game1.server != null)
            {
                Game1.server.sendMessages();
            }
            if (Game1.client != null)
            {
                Game1.client.sendMessages();
            }
        }

        public virtual void inviteAccepted()
        {
            if (Game1.activeClickableMenu is TitleMenu)
            {
                TitleMenu title = Game1.activeClickableMenu as TitleMenu;
                if (TitleMenu.subMenu == null)
                {
                    title.performButtonAction("Invite");
                    return;
                }
                if (TitleMenu.subMenu is FarmhandMenu || TitleMenu.subMenu is CoopMenu)
                {
                    TitleMenu.subMenu = new FarmhandMenu();
                }
            }
        }

        public virtual Client InitClient(Client client)
        {
            return client;
        }

        public virtual Server InitServer(Server server)
        {
            return server;
        }

        public static string MessageTypeToString(byte type)
        {
            switch (type)
            {
                case 0:
                    return "farmerDelta";
                case 1:
                    return "serverIntroduction";
                case 2:
                    return "playerIntroduction";
                case 3:
                    return "locationIntroduction";
                case 4:
                    return "forceEvent";
                case 5:
                    return "warpFarmer";
                case 6:
                    return "locationDelta";
                case 7:
                    return "locationSprites";
                case 8:
                    return "characterWarp";
                case 9:
                    return "availableFarmhands";
                case 10:
                    return "chatMessage";
                case 11:
                    return "connectionMessage";
                case 12:
                    return "worldDelta";
                case 13:
                    return "teamDelta";
                case 14:
                    return "newDaySync";
                case 15:
                    return "chatInfoMessage";
                case 16:
                    return "userNameUpdate";
                case 17:
                    return "farmerGainExperience";
                case 18:
                    return "serverToClientsMessage";
                case 19:
                    return "disconnecting";
                case 20:
                    return "sharedAchievement";
                case 21:
                    return "globalMessage";
                case 22:
                    return "partyWideMail";
                case 23:
                    return "forceKick";
                case 24:
                    return "removeLocationFromLookup";
                case 25:
                    return "farmerKilledMonster";
                case 26:
                    return "requestGrandpaReevaluation";
                default:
                    return type.ToString();
            }
        }

        public static readonly long AllPlayers = 0L;

        public const byte farmerDelta = 0;

        public const byte serverIntroduction = 1;

        public const byte playerIntroduction = 2;

        public const byte locationIntroduction = 3;

        public const byte forceEvent = 4;

        public const byte warpFarmer = 5;

        public const byte locationDelta = 6;

        public const byte locationSprites = 7;

        public const byte characterWarp = 8;

        public const byte availableFarmhands = 9;

        public const byte chatMessage = 10;

        public const byte connectionMessage = 11;

        public const byte worldDelta = 12;

        public const byte teamDelta = 13;

        public const byte newDaySync = 14;

        public const byte chatInfoMessage = 15;

        public const byte userNameUpdate = 16;

        public const byte farmerGainExperience = 17;

        public const byte serverToClientsMessage = 18;

        public const byte disconnecting = 19;

        public const byte sharedAchievement = 20;

        public const byte globalMessage = 21;

        public const byte partyWideMail = 22;

        public const byte forceKick = 23;

        public const byte removeLocationFromLookup = 24;

        public const byte farmerKilledMonster = 25;

        public const byte requestGrandpaReevaluation = 26;

        public const byte digBuriedNut = 27;

        public const byte requestPassout = 28;

        public const byte passout = 29;

        public int defaultInterpolationTicks = 15;

        public int farmerDeltaBroadcastPeriod = 3;

        public int locationDeltaBroadcastPeriod = 3;

        public int worldStateDeltaBroadcastPeriod = 3;

        public int playerLimit = 4;

        public static string kicked = "KICKED";

        public const string protocolVersion = "1.5.5";

        public readonly NetLogger logging = new NetLogger();

        protected List<long> disconnectingFarmers = new List<long>();

        public ulong latestID;

        public Dictionary<string, CachedMultiplayerMap> cachedMultiplayerMaps = new Dictionary<string, CachedMultiplayerMap>();

        public const string MSG_START_FESTIVAL_EVENT = "festivalEvent";

        public const string MSG_END_FESTIVAL = "endFest";

        public const string MSG_TRAIN_APPROACH = "trainApproach";

        public const string MSG_PLACEHOLDER = "[replace me]";

        public enum PartyWideMessageQueue
        {
            MailForTomorrow,
            SeenMail
        }

        private struct FarmerRoots : IEnumerable<NetFarmerRoot>, IEnumerable
        {
            public MobileMultiplayer.FarmerRoots.Enumerator GetEnumerator()
            {
                return new MobileMultiplayer.FarmerRoots.Enumerator(true);
            }

            IEnumerator<NetFarmerRoot> IEnumerable<NetFarmerRoot>.GetEnumerator()
            {
                return new MobileMultiplayer.FarmerRoots.Enumerator(true);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return new MobileMultiplayer.FarmerRoots.Enumerator(true);
            }

            public struct Enumerator : IEnumerator<NetFarmerRoot>, IEnumerator, IDisposable
            {
                public Enumerator(bool dummy)
                {
                    this._enumerator = Game1.otherFarmers.Roots.GetEnumerator();
                    this._current = null;
                    this._step = 0;
                    this._done = false;
                }

                public bool MoveNext()
                {
                    if (this._step == 0)
                    {
                        this._step++;
                        if (Game1.serverHost != null)
                        {
                            this._current = Game1.serverHost;
                            return true;
                        }
                    }
                    while (this._enumerator.MoveNext())
                    {
                        KeyValuePair<long, NetRoot<Farmer>> keyValuePair = this._enumerator.Current;
                        NetRoot<Farmer> root = keyValuePair.Value;
                        if (Game1.serverHost == null || root != Game1.serverHost)
                        {
                            this._current = (root as NetFarmerRoot);
                            return true;
                        }
                    }
                    this._done = true;
                    this._current = null;
                    return false;
                }

                public NetFarmerRoot Current
                {
                    get
                    {
                        return this._current;
                    }
                }

                public void Dispose()
                {
                }

                object IEnumerator.Current
                {
                    get
                    {
                        if (this._done)
                        {
                            throw new InvalidOperationException();
                        }
                        return this._current;
                    }
                }

                void IEnumerator.Reset()
                {
                    this._enumerator = Game1.otherFarmers.Roots.GetEnumerator();
                    this._current = null;
                    this._step = 0;
                    this._done = false;
                }

                private Dictionary<long, NetRoot<Farmer>>.Enumerator _enumerator;

                private NetFarmerRoot _current;

                private int _step;

                private bool _done;
            }
        }

        public struct ActiveLocations : IEnumerable<GameLocation>, IEnumerable
        {
            public Multiplayer.ActiveLocations.Enumerator GetEnumerator()
            {
                return default(Multiplayer.ActiveLocations.Enumerator);
            }

            IEnumerator<GameLocation> IEnumerable<GameLocation>.GetEnumerator()
            {
                return default(Multiplayer.ActiveLocations.Enumerator);
            }

            IEnumerator IEnumerable.GetEnumerator()
            {
                return default(Multiplayer.ActiveLocations.Enumerator);
            }

            public struct Enumerator : IEnumerator<GameLocation>, IEnumerator, IDisposable
            {
                public bool MoveNext()
                {
                    if (this._step == 0)
                    {
                        this._step++;
                        if (Game1.currentLocation != null)
                        {
                            this._current = Game1.currentLocation;
                            return true;
                        }
                    }
                    if (this._step == 1)
                    {
                        this._step++;
                        Farm farm = Game1.getFarm();
                        if (farm != null && farm != Game1.currentLocation)
                        {
                            this._current = farm;
                            return true;
                        }
                    }
                    if (this._step == 2)
                    {
                        this._step++;
                        GameLocation farmhouse = Game1.getLocationFromName("FarmHouse");
                        if (farmhouse != null && farmhouse != Game1.currentLocation)
                        {
                            this._current = farmhouse;
                            return true;
                        }
                    }
                    if (this._step == 3)
                    {
                        this._step++;
                        GameLocation greenhouse = Game1.getLocationFromName("Greenhouse");
                        if (greenhouse != null && greenhouse != Game1.currentLocation)
                        {
                            this._current = greenhouse;
                            return true;
                        }
                    }
                    if (this._step == 4)
                    {
                        this._step++;
                        Farm farm2 = Game1.getFarm();
                        this._enumerator = farm2.buildings.GetEnumerator();
                    }
                    while (this._enumerator.MoveNext())
                    {
                        GameLocation location = this._enumerator.Current.indoors.Value;
                        if (location != null && location != Game1.currentLocation)
                        {
                            this._current = location;
                            return true;
                        }
                    }
                    this._done = true;
                    this._current = null;
                    return false;
                }

                public GameLocation Current
                {
                    get
                    {
                        return this._current;
                    }
                }

                public void Dispose()
                {
                }

                object IEnumerator.Current
                {
                    get
                    {
                        if (this._done)
                        {
                            throw new InvalidOperationException();
                        }
                        return this._current;
                    }
                }

                void IEnumerator.Reset()
                {
                    this._current = null;
                    this._step = 0;
                    this._done = false;
                }

                private List<Building>.Enumerator _enumerator;

                private GameLocation _current;

                private int _step;

                private bool _done;
            }
        }
        public enum DisconnectType
        {
            None,
            ClosedGame,
            ExitedToMainMenu,
            ExitedToMainMenu_FromFarmhandSelect,
            HostLeft,
            ServerOfflineMode,
            ServerFull,
            Kicked,
            AcceptedOtherInvite,
            ClientTimeout,
            LidgrenTimeout,
            GalaxyTimeout,
            Timeout_FarmhandSelection,
            LidgrenDisconnect_Unknown
        }
    }
}
