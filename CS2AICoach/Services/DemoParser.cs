using System.IO.Compression;
using ICSharpCode.SharpZipLib.BZip2;
using CS2AICoach.Models;
using DemoFile;
using DemoFile.Game.Cs;
using System.Security.AccessControl;

namespace CS2AICoach.Services
{
    public class DemoParser : IDisposable
    {
        private class PlayerIdentifier
        {
            internal static readonly Dictionary<(long SteamID, float Tick), int> _sequenceNumbers = new();
            private static readonly object _lockObj = new();

            public long SteamID { get; set; }
            public string Name { get; set; } = "";
            public int Team { get; set; }
            public float Tick { get; set; }
            public int SequenceNumber { get; private set; }

            public static PlayerIdentifier Create(long steamId, string name, int team, float tick)
            {
                lock (_lockObj)
                {
                    var key = (steamId, tick);
                    if (!_sequenceNumbers.ContainsKey(key))
                    {
                        _sequenceNumbers[key] = 0;
                    }
                    int sequence = _sequenceNumbers[key]++;

                    return new PlayerIdentifier
                    {
                        SteamID = steamId,
                        Name = name,
                        Team = team,
                        Tick = tick,
                        SequenceNumber = sequence
                    };
                }
            }

            public override bool Equals(object? obj)
            {
                if (obj is PlayerIdentifier other)
                {
                    return SteamID == other.SteamID &&
                           Tick == other.Tick &&
                           SequenceNumber == other.SequenceNumber;
                }
                return false;
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(SteamID, Tick, SequenceNumber);
            }

            public override string ToString()
            {
                return $"Player[SteamID={SteamID}, Name={Name}, Team={Team}, Tick={Tick}, Seq={SequenceNumber}]";
            }
        }

        private readonly string _demoFilePath;
        private readonly MatchData _matchData;
        private Dictionary<PlayerIdentifier, PlayerStats> _playerStats;
        private readonly CsDemoParser _demo;
        private Dictionary<string, int> _playerTeams;
        private Dictionary<string, float> _playerMoney;
        private Dictionary<string, float> _playerEquipmentValue;
        private Dictionary<string, HashSet<string>> _playerInventory;
        private Dictionary<string, int> _roundStartMoney;
        private Dictionary<string, bool> _playerHasArmor;
        private Dictionary<string, bool> _playerHasHelmet;
        private HashSet<string> _flashedPlayers;
        private Dictionary<string, float> _flashDurations;
        private Dictionary<string, GameEvent> _lastDeathByPlayer;

        public DemoParser(string demoFilePath, CsDemoParser demo)
        {
            _demoFilePath = demoFilePath;
            _demo = demo;
            _matchData = new MatchData();
            _playerStats = new Dictionary<PlayerIdentifier, PlayerStats>();
            _playerTeams = new Dictionary<string, int>();
            _playerMoney = new Dictionary<string, float>();
            _playerEquipmentValue = new Dictionary<string, float>();
            _playerInventory = new Dictionary<string, HashSet<string>>();
            _roundStartMoney = new Dictionary<string, int>();
            _playerHasArmor = new Dictionary<string, bool>();
            _playerHasHelmet = new Dictionary<string, bool>();
            _flashedPlayers = new HashSet<string>();
            _flashDurations = new Dictionary<string, float>();
            _lastDeathByPlayer = new Dictionary<string, GameEvent>();
        }

        private PlayerIdentifier GetPlayerIdentifier(long steamId, string playerName)
        {
            var tick = _demo.CurrentDemoTick.Value;
            var team = _playerTeams.GetValueOrDefault(playerName, 0);

            var identifier = PlayerIdentifier.Create(steamId, playerName, team, tick);
            Logger.Log($"Created identifier: {identifier}");
            return identifier;
        }

        private PlayerStats GetOrCreatePlayerStats(long steamId, string playerName)
        {
            var identifier = GetPlayerIdentifier(steamId, playerName);

            if (!_playerStats.ContainsKey(identifier))
            {
                _playerStats[identifier] = new PlayerStats
                {
                    Name = playerName,
                    SteamId = steamId.ToString()
                };
            }
            return _playerStats[identifier];
        }

        private WeaponStats GetOrCreateWeaponStats(PlayerStats playerStats, string weaponName)
        {
            var weaponStats = playerStats.WeaponUsage
                .FirstOrDefault(w => w.WeaponName == weaponName);

            if (weaponStats == null)
            {
                weaponStats = new WeaponStats { WeaponName = weaponName };
                playerStats.WeaponUsage.Add(weaponStats);
            }

            return weaponStats;
        }

        private void UpdatePlayerStats(
            long killerId,
            long victimId,
            string killerName,
            string victimName,
            string killerSteamId,
            string victimSteamId,
            string weapon,
            bool headshot)
        {
            Logger.Log($"Updating stats for kill: {killerName} -> {victimName}");

            var killerIdentifier = GetPlayerIdentifier(killerId, killerName);
            var victimIdentifier = GetPlayerIdentifier(victimId, victimName);

            // Get or create killer stats
            if (!_playerStats.TryGetValue(killerIdentifier, out var killerStats))
            {
                killerStats = new PlayerStats
                {
                    Name = killerName,
                    SteamId = killerSteamId,
                    Data = new Dictionary<string, object>()
                };
                _playerStats[killerIdentifier] = killerStats;
                Logger.Log($"Created new stats entry for killer: {killerIdentifier}");
            }

            // Get or create victim stats
            if (!_playerStats.TryGetValue(victimIdentifier, out var victimStats))
            {
                victimStats = new PlayerStats
                {
                    Name = victimName,
                    SteamId = victimSteamId,
                    Data = new Dictionary<string, object>()
                };
                _playerStats[victimIdentifier] = victimStats;
                Logger.Log($"Created new stats entry for victim: {victimIdentifier}");
            }

            // Update stats
            killerStats.Kills++;
            victimStats.Deaths++;

            const string HEADSHOTS_KEY = "headshots_count";
            if (!killerStats.Data.ContainsKey(HEADSHOTS_KEY))
            {
                killerStats.Data[HEADSHOTS_KEY] = 0;
            }

            if (headshot)
            {
                killerStats.Data[HEADSHOTS_KEY] = (int)killerStats.Data[HEADSHOTS_KEY] + 1;
            }

            killerStats.HeadshotPercentage = killerStats.Kills > 0 ?
                ((int)killerStats.Data[HEADSHOTS_KEY]) * 100.0 / killerStats.Kills : 0;

            var weaponStats = killerStats.WeaponUsage
                .FirstOrDefault(w => w.WeaponName == weapon);

            if (weaponStats == null)
            {
                weaponStats = new WeaponStats { WeaponName = weapon };
                killerStats.WeaponUsage.Add(weaponStats);
            }

            weaponStats.Kills++;

            // Track flash kills with a consistent key
            const string FLASH_KILLS_KEY = "flash_kills_count";
            if (_flashedPlayers.Contains(victimName) &&
                _flashDurations.TryGetValue(victimName, out float flashDuration) &&
                flashDuration >= 0.7f)
            {
                if (!killerStats.Data.ContainsKey(FLASH_KILLS_KEY))
                {
                    killerStats.Data[FLASH_KILLS_KEY] = 0;
                }
                killerStats.Data[FLASH_KILLS_KEY] = (int)killerStats.Data[FLASH_KILLS_KEY] + 1;
            }
        }

        private void ClearSequenceNumbers()
        {
            // Clear the sequence numbers dictionary between parses
            var field = typeof(PlayerIdentifier).GetField("_sequenceNumbers",
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Static);

            if (field != null)
            {
                var dict = field.GetValue(null) as Dictionary<(long SteamID, float Tick), int>;
                dict?.Clear();
            }
        }

        public async Task<MatchData> ParseDemo()
        {
            try
            {
                // Clear sequence numbers for both PlayerIdentifier and GameEvent
                ClearSequenceNumbers();
                GameEvent.ClearSequenceNumbers();

                SubscribeToEvents();
                byte[] demoBytes = await ReadDemoFile();

                using (var demoStream = new MemoryStream(demoBytes))
                {
                    var reader = DemoFileReader.Create(_demo, demoStream);
                    await reader.ReadAllAsync();
                }

                // Add logging to track the stats we're processing
                Logger.Log($"Total player stats entries: {_playerStats.Count}");
                foreach (var kvp in _playerStats)
                {
                    Logger.Log($"Player: {kvp.Key.Name}, SteamID: {kvp.Key.SteamID}, Team: {kvp.Key.Team}, Tick: {kvp.Key.Tick}, Sequence: {kvp.Key.SequenceNumber}");
                }

                // Group stats by SteamID first
                var steamIdGroups = _playerStats
                    .GroupBy(kvp => kvp.Key.SteamID)
                    .ToDictionary(
                        g => g.Key,
                        g => g.OrderBy(kvp => kvp.Key.Tick)  // Order by Tick instead of FirstSeenTick
                              .ThenBy(kvp => kvp.Key.SequenceNumber)  // Then by sequence number for same-tick events
                              .Select(kvp => kvp.Value)
                              .ToList()
                    );

                Logger.Log($"Number of unique SteamIDs: {steamIdGroups.Count}");

                // Create intermediate dictionary to store merged stats
                var mergedStats = new Dictionary<string, PlayerStats>();

                foreach (var group in steamIdGroups)
                {
                    var steamId = group.Key.ToString();
                    Logger.Log($"Processing group for SteamID {steamId} with {group.Value.Count} entries");

                    // Skip empty groups
                    if (!group.Value.Any()) continue;

                    // Merge all stats for this SteamID
                    var mergedPlayerStats = MergePlayerStats(group.Value);

                    // Use SteamID as the key to prevent collisions
                    if (!mergedStats.ContainsKey(steamId))
                    {
                        mergedStats[steamId] = mergedPlayerStats;
                        Logger.Log($"Added merged stats for {mergedPlayerStats.Name} (SteamID: {steamId})");
                    }
                    else
                    {
                        Logger.Log($"WARNING: Duplicate SteamID found: {steamId}");
                    }
                }

                _matchData.PlayerStats = mergedStats;
                return _matchData;
            }
            catch (Exception ex)
            {
                Logger.Log($"Detailed error in ParseDemo: {ex.Message}");
                Logger.Log($"Stack trace: {ex.StackTrace}");
                throw;
            }
            finally
            {
                ClearSequenceNumbers();
                GameEvent.ClearSequenceNumbers();
            }
        }

        private PlayerStats MergePlayerStats(IEnumerable<PlayerStats> statsToMerge)
        {
            if (!statsToMerge.Any())
            {
                throw new ArgumentException("No stats to merge");
            }

            // Take the most recent name for the player (last entry)
            var lastStats = statsToMerge.Last();
            var mergedStats = new PlayerStats
            {
                Name = lastStats.Name,
                SteamId = lastStats.SteamId,
                Kills = statsToMerge.Sum(s => s.Kills),
                Deaths = statsToMerge.Sum(s => s.Deaths),
                Assists = statsToMerge.Sum(s => s.Assists),
                HeadshotPercentage = statsToMerge.Where(s => s.Kills > 0)
                                                .DefaultIfEmpty(lastStats)
                                                .Average(s => s.HeadshotPercentage),
                Data = new Dictionary<string, object>(),
                WeaponUsage = new List<WeaponStats>()
            };

            Logger.Log($"Merged stats for SteamID {lastStats.SteamId}:");
            Logger.Log($"  - Name: {mergedStats.Name}");
            Logger.Log($"  - Total Kills: {mergedStats.Kills}");
            Logger.Log($"  - Total Deaths: {mergedStats.Deaths}");

            // Merge weapon usage stats
            var weaponGroups = statsToMerge
                .SelectMany(s => s.WeaponUsage)
                .GroupBy(w => w.WeaponName);

            foreach (var weaponGroup in weaponGroups)
            {
                var mergedWeapon = new WeaponStats
                {
                    WeaponName = weaponGroup.Key,
                    Kills = weaponGroup.Sum(w => w.Kills),
                    TotalShots = weaponGroup.Sum(w => w.TotalShots),
                    Hits = weaponGroup.Sum(w => w.Hits)
                };
                mergedStats.WeaponUsage.Add(mergedWeapon);
            }

            // Merge Data dictionary
            foreach (var stats in statsToMerge)
            {
                foreach (var kvp in stats.Data)
                {
                    if (!mergedStats.Data.ContainsKey(kvp.Key))
                    {
                        mergedStats.Data[kvp.Key] = kvp.Value;
                    }
                    else if (kvp.Value is int intValue)
                    {
                        // Sum numeric values
                        if (mergedStats.Data[kvp.Key] is int existingValue)
                        {
                            mergedStats.Data[kvp.Key] = existingValue + intValue;
                        }
                    }
                    // For non-numeric values, keep the most recent one
                    else
                    {
                        mergedStats.Data[kvp.Key] = kvp.Value;
                    }
                }
            }

            return mergedStats;
        }

        private void AddEvent(string type, Dictionary<string, object> data)
        {
            if (type == "PlayerFlashed")
            {
                Logger.Log($"Adding PlayerFlashed event -- Tick: {_demo.CurrentDemoTick.Value}");
            }

            var gameEvent = GameEvent.Create(
                type,
                _demo.CurrentDemoTick.Value,
                data
            );

            Logger.Log($"Created event: {gameEvent}");
            _matchData.Events.Add(gameEvent);
        }

        private void SubscribeToEvents()
        {
            _demo.PacketEvents.SvcServerInfo += OnServerInfo;

            // Core gameplay events
            _demo.Source1GameEvents.PlayerDeath += OnPlayerDeath;
            _demo.Source1GameEvents.WeaponFire += OnWeaponFire;
            _demo.Source1GameEvents.PlayerHurt += OnPlayerHurt;
            _demo.Source1GameEvents.RoundStart += OnRoundStart;
            _demo.Source1GameEvents.RoundEnd += OnRoundEnd;

            // Economy events
            _demo.Source1GameEvents.ItemPickup += OnItemPickup;
            _demo.Source1GameEvents.ItemRemove += OnItemDrop;
            _demo.Source1GameEvents.ItemEquip += OnItemEquip;

            // Money-related events - using AdjustMoney from UserMessageEvents
            _demo.UserMessageEvents.AdjustMoney += OnAdjustMoney;
            _demo.Source1GameEvents.PlayerSpawn += OnPlayerSpawn;

            // Team events
            _demo.Source1GameEvents.PlayerTeam += OnPlayerTeam;
            _demo.Source1GameEvents.PlayerDisconnect += OnPlayerDisconnect;

            // Utility events
            _demo.Source1GameEvents.FlashbangDetonate += OnFlashDetonate;
            _demo.Source1GameEvents.HegrenadeDetonate += OnHEGrenadeDetonate;
            _demo.Source1GameEvents.MolotovDetonate += OnMolotovDetonate;
            _demo.Source1GameEvents.PlayerBlind += OnPlayerBlind;
        }

        private void UnsubscribeFromEvents()
        {
            // Server info
            _demo.PacketEvents.SvcServerInfo -= OnServerInfo;

            // Core gameplay events
            _demo.Source1GameEvents.PlayerDeath -= OnPlayerDeath;
            _demo.Source1GameEvents.WeaponFire -= OnWeaponFire;
            _demo.Source1GameEvents.PlayerHurt -= OnPlayerHurt;
            _demo.Source1GameEvents.RoundStart -= OnRoundStart;
            _demo.Source1GameEvents.RoundEnd -= OnRoundEnd;

            // Economy events
            _demo.Source1GameEvents.ItemPickup -= OnItemPickup;
            _demo.Source1GameEvents.ItemRemove -= OnItemDrop;
            _demo.Source1GameEvents.ItemEquip -= OnItemEquip;

            // Money-related events
            _demo.UserMessageEvents.AdjustMoney -= OnAdjustMoney;
            _demo.Source1GameEvents.PlayerSpawn -= OnPlayerSpawn;

            // Team events
            _demo.Source1GameEvents.PlayerTeam -= OnPlayerTeam;
            _demo.Source1GameEvents.PlayerDisconnect -= OnPlayerDisconnect;

            // Utility events
            _demo.Source1GameEvents.FlashbangDetonate -= OnFlashDetonate;
            _demo.Source1GameEvents.HegrenadeDetonate -= OnHEGrenadeDetonate;
            _demo.Source1GameEvents.MolotovDetonate -= OnMolotovDetonate;
            _demo.Source1GameEvents.PlayerBlind -= OnPlayerBlind;
        }

        private void OnServerInfo(CSVCMsg_ServerInfo e)
        {
            _matchData.MapName = e.MapName;
            _matchData.TickRate = e.TickInterval;
        }

        private void OnPlayerTeam(Source1PlayerTeamEvent e)
        {
            if (e.Player?.PlayerName == null) return;
            _playerTeams[e.Player.PlayerName] = e.Team;
        }

        private int DeterminePlayerTeam(IEnumerable<GameEvent> events, string playerName)
        {
            var playerEvent = events.FirstOrDefault(e =>
                e.Type == "PlayerDeath" &&
                (e.Data["KillerName"].ToString() == playerName ||
                 e.Data["VictimName"].ToString() == playerName));

            if (playerEvent == null)
                return 0;

            if (playerEvent.Data["KillerName"].ToString() == playerName)
                return Convert.ToInt32(playerEvent.Data["KillerTeam"]);
            else
                return Convert.ToInt32(playerEvent.Data["VictimTeam"]);
        }

        private void OnWeaponFire(Source1WeaponFireEvent e)
        {
            if (e.Player?.SteamID == null || e.Player?.PlayerName == null) return;

            var playerStats = GetOrCreatePlayerStats((long)e.Player.SteamID, e.Player.PlayerName);
            var weaponStats = GetOrCreateWeaponStats(playerStats, e.Weapon ?? "unknown");
            weaponStats.TotalShots++;

            var data = new Dictionary<string, object>
            {
                { "PlayerName", e.Player.PlayerName },
                { "Weapon", e.Weapon ?? "unknown" }
            };

            AddEvent("WeaponFire", data);
        }

        private void OnPlayerHurt(Source1PlayerHurtEvent e)
        {
            if (e.Attacker?.SteamID == null || e.Attacker?.PlayerName == null) return;

            var playerStats = GetOrCreatePlayerStats((long)e.Attacker.SteamID, e.Attacker.PlayerName);
            var weaponStats = GetOrCreateWeaponStats(playerStats, e.Weapon ?? "unknown");
            weaponStats.RegisterHit();
            var data = new Dictionary<string, object>
            {
                { "AttackerName", e.Attacker.PlayerName },
                { "VictimName", e.Player?.PlayerName ?? "Unknown" },
                { "Weapon", e.Weapon ?? "unknown" },
                { "Damage", e.DmgHealth},
                { "ArmorDamage", e.DmgArmor},
                { "HealthRemaining", e.Health }
            };

            AddEvent("PlayerHurt", data);
        }

        // TODO: Figure out how to associate a money event with a player
        private void OnAdjustMoney(CCSUsrMsg_AdjustMoney e)
        {
            if (e.Amount == default) return;

            // TODO: Fix this, comparing to 0 is not actually usable
            var player = _demo.Players.FirstOrDefault(p => p.SteamID == 0);

            if (player?.PlayerName == null) return;

            string playerName = player.PlayerName;
            _playerMoney[playerName] = e.Amount;

            var data = new Dictionary<string, object>
            {
                { "PlayerName", playerName },
                { "Amount", e.Amount },
                { "Change", e.Amount }
            };

            AddEvent("PlayerMoney", data);
        }

        private void OnPlayerSpawn(Source1PlayerSpawnEvent e)
        {
            if (e.Player == null) return;

            string playerName = e.Player.PlayerName ?? "Unknown";

            if (!_playerInventory.ContainsKey(playerName))
            {
                _playerInventory[playerName] = new HashSet<string>();
            }
            else
            {
                _playerInventory[playerName].Clear();
            }

            _playerHasArmor[playerName] = false;
            _playerHasHelmet[playerName] = false;

            var data = new Dictionary<string, object>
            {
                { "PlayerName", playerName },
                { "Team", e.Player.Team }
            };

            AddEvent("PlayerSpawn", data);
        }

        private void OnItemEquip(Source1ItemEquipEvent e)
        {
            if (e.Player == null) return;

            string playerName = e.Player.PlayerName ?? "Unknown";
            string item = e.Item ?? "Unknown";

            if (!_playerInventory.ContainsKey(playerName))
            {
                _playerInventory[playerName] = new HashSet<string>();
            }

            _playerInventory[playerName].Add(item);

            if (item.Equals("vest", StringComparison.OrdinalIgnoreCase))
            {
                _playerHasArmor[playerName] = true;
            }
            else if (item.Equals("vesthelm", StringComparison.OrdinalIgnoreCase))
            {
                _playerHasArmor[playerName] = true;
                _playerHasHelmet[playerName] = true;
            }

            var data = new Dictionary<string, object>
            {
                { "PlayerName", playerName },
                { "Item", item }
            };

            AddEvent("ItemEquip", data);
        }

        private void OnItemPickup(Source1ItemPickupEvent e)
        {
            if (e.Player == null) return;

            string playerName = e.Player.PlayerName ?? "Unknown";
            string item = e.Item ?? "Unknown";

            if (!_playerInventory.ContainsKey(playerName))
            {
                _playerInventory[playerName] = new HashSet<string>();
            }

            _playerInventory[playerName].Add(item);
            UpdatePlayerEquipmentValue(playerName);

            var data = new Dictionary<string, object>
            {
                { "PlayerName", playerName },
                { "Item", item },
                { "RemainingEquipmentValue", _playerEquipmentValue.TryGetValue(playerName, out var remainingEquipmentValue) ? remainingEquipmentValue : CalculatePlayerEquipmentValue(playerName) }
            };

            AddEvent("ItemPickup", data);
        }

        private void OnItemDrop(Source1ItemRemoveEvent e)
        {
            if (e.Player == null) return;

            string playerName = e.Player.PlayerName ?? "Unknown";
            string weapon = e.Item ?? "Unknown";

            if (_playerInventory.ContainsKey(playerName))
            {
                _playerInventory[playerName].Remove(weapon);
            }

            UpdatePlayerEquipmentValue(playerName);

            bool isValuableWeapon = GetItemValue(weapon) >= 2000;
            var data = new Dictionary<string, object>
            {
                { "PlayerName", playerName },
                { "Weapon", weapon },
                { "IsValuable", isValuableWeapon },
                { "WeaponValue", GetItemValue(weapon) },
                { "RemainingEquipmentValue", CalculatePlayerEquipmentValue(playerName) }
            };

            AddEvent("WeaponDrop", data);
        }

        private void OnFlashDetonate(Source1FlashbangDetonateEvent e)
        {
            if (e.Player?.PlayerName == null) return;

            var data = new Dictionary<string, object>
            {
                { "ThrowerName", e.Player.PlayerName },
                { "Position", new { X = e.X, Y = e.Y, Z = e.Z } },
                { "Tick", _demo.CurrentDemoTick.Value }  // Add tick for proper time-based tracking
            };

            AddEvent("FlashDetonate", data);
        }

        private void OnPlayerBlind(Source1PlayerBlindEvent e)
        {
            if (e.Player == null || e.Attacker == null) return;

            string victimName = e.Player.PlayerName ?? "Unknown";
            _flashedPlayers.Add(victimName);
            _flashDurations[victimName] = e.BlindDuration;

            var data = new Dictionary<string, object>
            {
                { "VictimName", victimName },
                { "AttackerName", e.Attacker.PlayerName ?? "Unknown" },
                { "FlashDuration", e.BlindDuration },
                { "Tick", _demo.CurrentDemoTick.Value }  // Add tick for proper time-based tracking
            };

            AddEvent("PlayerFlashed", data);
        }

        private void OnHEGrenadeDetonate(Source1HegrenadeDetonateEvent e)
        {
            if (e.Player?.PlayerName == null) return;  // Changed from e.Thrower

            var data = new Dictionary<string, object>
            {
                { "ThrowerName", e.Player.PlayerName ?? "Unknown" },
                { "Position", new { X = e.X, Y = e.Y, Z = e.Z } }
            };

            AddEvent("HEGrenadeDetonate", data);
        }

        private void OnMolotovDetonate(Source1MolotovDetonateEvent e)
        {
            if (e.Player?.PlayerName == null) return;

            var data = new Dictionary<string, object>
            {
                { "ThrowerName", e.Player.PlayerName },
                { "Position", new { X = e.X, Y = e.Y, Z = e.Z } }
            };
            AddEvent("MolotovDetonate", data);
        }

        private void OnPlayerDisconnect(Source1PlayerDisconnectEvent e)
        {
            if (e.Player == null) return;

            string playerName = e.Player.PlayerName ?? "Unknown";

            // Clean up player tracking
            _playerMoney.Remove(playerName);
            _playerEquipmentValue.Remove(playerName);
            _playerInventory.Remove(playerName);
            _roundStartMoney.Remove(playerName);
            _playerHasArmor.Remove(playerName);
            _playerHasHelmet.Remove(playerName);

            var data = new Dictionary<string, object>
            {
                { "PlayerName", playerName }
            };

            AddEvent("PlayerDisconnect", data);
        }

        private bool IsTradeKill(Source1PlayerDeathEvent deathEvent)
        {
            const int TRADE_WINDOW_TICKS = 128; // 2 seconds at 64 tick
            if (deathEvent.Player?.PlayerName == null) return false;

            var currentTick = _demo.CurrentDemoTick.Value;
            var currentVictim = deathEvent.Player.PlayerName;
            var currentKiller = deathEvent.Attacker?.PlayerName ?? "";

            // Get all relevant death events within the trade window
            var relevantDeaths = _matchData.Events
                .Where(e => e.Type == "PlayerDeath" &&
                           e.Tick >= currentTick - TRADE_WINDOW_TICKS &&
                           e.Tick <= currentTick)
                .OrderBy(e => e.Tick)
                .ThenBy(e => e.SequenceNumber)
                .ToList();

            // Look for previous deaths that could be traded
            foreach (var previousDeath in relevantDeaths)
            {
                string previousKiller = previousDeath.Data["KillerName"].ToString() ?? "";
                string previousVictim = previousDeath.Data["VictimName"].ToString() ?? "";

                if (previousVictim == currentVictim)
                    continue;

                // Check if this death is trading a teammate's death
                bool isTeammateDeath = IsOnSameTeam(
                    previousVictim,
                    currentVictim,
                    Convert.ToInt32(previousDeath.Data["VictimTeam"]));

                if (isTeammateDeath && previousKiller == currentVictim)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsOnSameTeam(string player1, string player2, int referenceTeam)
        {
            // DeterminePlayerTeam(????)
            // This could be enhanced with proper team tracking
            return true; // Placeholder - implement based on your team tracking
        }

        private void OnRoundEnd(Source1RoundEndEvent e)
        {
            var data = new Dictionary<string, object>
            {
                { "Winner", e.Winner },
                { "Reason", e.Reason },
                { "Message", e.Message }
            };

            // Add final equipment values for all players
            foreach (var (playerName, _) in _playerInventory)
            {
                data[$"FinalEquipment_{playerName}"] = CalculatePlayerEquipmentValue(playerName);
                data[$"FinalMoney_{playerName}"] = _playerMoney.GetValueOrDefault(playerName, 0);
            }

            AddEvent("RoundEnd", data);
        }
        private void OnRoundStart(Source1RoundStartEvent e)
        {
            // Add debug logging
            Logger.Log("Round Start - Current Players:");
            foreach (var (playerName, team) in _playerTeams)
            {
                Logger.Log($"- {playerName}: Team {team}");
            }
            // Reset tracking collections
            _flashedPlayers.Clear();
            _flashDurations.Clear();
            _lastDeathByPlayer.Clear();

            Dictionary<string, object> roundStartData = new();

            // Calculate team money totals for bonus round detection
            var teamMoney = new Dictionary<int, float>();
            foreach (var (playerName, money) in _playerMoney)
            {
                int team = _playerTeams.GetValueOrDefault(playerName, 0);
                roundStartData[$"Money_{playerName}"] = money;
                roundStartData[$"Equipment_{playerName}"] = CalculatePlayerEquipmentValue(playerName);
                if (!roundStartData.ContainsKey($"Team_{playerName}"))
                {
                    roundStartData[$"Team_{playerName}"] = team;
                }

                if (!teamMoney.ContainsKey(team))
                {
                    teamMoney[team] = 0;
                }
                else
                {
                    teamMoney[team] += money;
                }
            }

            // Determine if it's a bonus round (one team has significantly more money)
            bool isBonus = false;
            if (teamMoney.Count == 2)
            {
                var moneyDiff = Math.Abs(teamMoney.ElementAt(0).Value - teamMoney.ElementAt(1).Value);
                isBonus = moneyDiff > 10000; // $10,000 difference threshold
            }

            roundStartData["IsBonus"] = isBonus;

            AddEvent("RoundStart", roundStartData);
        }

        private void OnPlayerDeath(Source1PlayerDeathEvent e)
        {
            if (e.Player == null || e.Attacker == null) return;

            string victimName = e.Player.PlayerName ?? "Unknown";
            string attackerName = e.Attacker.PlayerName ?? "Unknown";
            string victimSteamId = e.Player.SteamID.ToString();
            string attackerSteamId = e.Attacker.SteamID.ToString();

            var isTradeKill = IsTradeKill(e);

            UpdatePlayerStats(
                (long)e.Attacker.SteamID,
                (long)e.Player.SteamID,
                attackerName,
                victimName,
                attackerSteamId,
                victimSteamId,
                e.Weapon ?? "Unknown",
                e.Headshot);

            var eventData = new Dictionary<string, object>
            {
                ["KillerName"] = attackerName,
                ["KillerSteamId"] = attackerSteamId,
                ["KillerTeam"] = _playerTeams.GetValueOrDefault(attackerName, 0),
                ["VictimName"] = victimName,
                ["VictimSteamId"] = victimSteamId,
                ["VictimTeam"] = _playerTeams.GetValueOrDefault(victimName, 0),
                ["Weapon"] = e.Weapon ?? "Unknown",
                ["Headshot"] = e.Headshot,
                ["IsTradeKill"] = isTradeKill,
                ["WasFlashed"] = _flashedPlayers.Contains(victimName) &&
                                _flashDurations.TryGetValue(victimName, out float flashDuration) &&
                                flashDuration >= 0.7f
            };

            AddEvent("PlayerDeath", eventData);
        }

        private float CalculatePlayerEquipmentValue(string playerName)
        {
            if (!_playerInventory.ContainsKey(playerName))
                return 0;

            float total = 0;

            // Add weapon values
            foreach (string item in _playerInventory[playerName])
            {
                total += GetItemValue(item);
            }

            // Add armor value
            if (_playerHasArmor.GetValueOrDefault(playerName, false))
            {
                total += _playerHasHelmet.GetValueOrDefault(playerName, false) ? 1000 : 650;
            }

            return total;
        }

        private float GetItemValue(string item)
        {
            return item.ToLower() switch
            {
                // Rifles
                "ak47" => 2700,
                "m4a1" or "m4a4" => 3100,
                "awp" => 4750,
                "aug" => 3300,
                "sg556" => 3000,
                "famas" => 2050,
                "galilar" => 1800,

                // SMGs
                "mp9" => 1250,
                "mac10" => 1050,
                "mp7" => 1500,
                "ump45" => 1200,
                "p90" => 2350,

                // Pistols
                "deagle" => 700,
                "usp" or "glock" => 200,
                "p250" => 300,
                "tec9" or "fiveseven" => 500,

                // Utility
                "hegrenade" => 300,
                "flashbang" => 200,
                "smokegrenade" => 300,
                "molotov" or "incgrenade" => 400,

                // Armor
                "vest" => 650,
                "vesthelm" => 1000,

                _ => 0
            };
        }

        private void UpdatePlayerEquipmentValue(string playerName)
        {
            _playerEquipmentValue[playerName] = CalculatePlayerEquipmentValue(playerName);
        }

        private async Task<byte[]> ReadDemoFile()
        {
            using (var fileStream = File.OpenRead(_demoFilePath))
            {
                if (_demoFilePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
                    using var memoryStream = new MemoryStream();
                    await gzipStream.CopyToAsync(memoryStream);
                    return memoryStream.ToArray();
                }
                else if (_demoFilePath.EndsWith(".bz2", StringComparison.OrdinalIgnoreCase))
                {
                    using var bzip2Stream = new BZip2InputStream(fileStream);
                    using var memoryStream = new MemoryStream();
                    await bzip2Stream.CopyToAsync(memoryStream);
                    return memoryStream.ToArray();
                }
                else
                {
                    using var memoryStream = new MemoryStream();
                    await fileStream.CopyToAsync(memoryStream);
                    return memoryStream.ToArray();
                }
            }
        }

        public void Dispose()
        {
            UnsubscribeFromEvents();
        }
    }
}