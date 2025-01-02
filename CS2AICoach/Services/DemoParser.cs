using System.IO.Compression;
using ICSharpCode.SharpZipLib.BZip2;
using CS2AICoach.Models;
using DemoFile;
using DemoFile.Game.Cs;

namespace CS2AICoach.Services
{
    public class DemoParser : IDisposable
    {
        private readonly string _demoFilePath;
        private readonly MatchData _matchData;
        private Dictionary<long, PlayerStats> _playerStats;
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
            _playerStats = new Dictionary<long, PlayerStats>();
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

        public async Task<MatchData> ParseDemo()
        {
            SubscribeToEvents();

            byte[] demoBytes = await ReadDemoFile();

            using (var demoStream = new MemoryStream(demoBytes))
            {
                var reader = DemoFileReader.Create(_demo, demoStream);
                await reader.ReadAllAsync();
            }

            _matchData.PlayerStats = _playerStats;
            return _matchData;
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

        private void OnWeaponFire(Source1WeaponFireEvent e)
        {
            if (e.Player?.SteamID == null || e.Player?.PlayerName == null) return;

            var playerStats = GetOrCreatePlayerStats((long)e.Player.SteamID, e.Player.PlayerName);
            var weaponStats = GetOrCreateWeaponStats(playerStats, e.Weapon ?? "unknown");
            weaponStats.TotalShots++;

            _matchData.Events.Add(new GameEvent
            {
                Type = "WeaponFire",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
                {
                    { "PlayerName", e.Player.PlayerName },
                    { "Weapon", e.Weapon ?? "unknown" }
                }
            });
        }

        private void OnPlayerHurt(Source1PlayerHurtEvent e)
        {
            if (e.Attacker?.SteamID == null || e.Attacker?.PlayerName == null) return;

            var playerStats = GetOrCreatePlayerStats((long)e.Attacker.SteamID, e.Attacker.PlayerName);
            var weaponStats = GetOrCreateWeaponStats(playerStats, e.Weapon ?? "unknown");
            weaponStats.RegisterHit();

            _matchData.Events.Add(new GameEvent
            {
                Type = "PlayerHurt",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
                {
                    { "AttackerName", e.Attacker.PlayerName },
                    { "VictimName", e.Player?.PlayerName ?? "Unknown" },
                    { "Weapon", e.Weapon ?? "unknown" },
                    { "Damage", e.DmgHealth},
                    { "ArmorDamage", e.DmgArmor},
                    { "HealthRemaining", e.Health }
                }
            });
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

            _matchData.Events.Add(new GameEvent
            {
                Type = "PlayerMoney",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
                {
                    { "PlayerName", playerName },
                    { "Amount", e.Amount },
                    { "Change", e.Amount }
                }
            });
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

            _matchData.Events.Add(new GameEvent
            {
                Type = "PlayerSpawn",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
                {
                    { "PlayerName", playerName },
                    { "Team", "Unknown/Fixme" } // TODO: Fix this
                }
            });
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

            _matchData.Events.Add(new GameEvent
            {
                Type = "ItemEquip",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
                {
                    { "PlayerName", playerName },
                    { "Item", item }
                }
            });
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

            _matchData.Events.Add(new GameEvent
            {
                Type = "ItemPickup",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
                {
                    { "PlayerName", playerName },
                    { "Item", item },
                    { "RemainingEquipmentValue", CalculatePlayerEquipmentValue(playerName) }
                }
            });
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

            _matchData.Events.Add(new GameEvent
            {
                Type = "WeaponDrop",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
                {
                    { "PlayerName", playerName },
                    { "Weapon", weapon },
                    { "IsValuable", isValuableWeapon },
                    { "WeaponValue", GetItemValue(weapon) },
                    { "RemainingEquipmentValue", CalculatePlayerEquipmentValue(playerName) }
                }
            });
        }

        private void OnFlashDetonate(Source1FlashbangDetonateEvent e)
        {
            if (e.Player?.PlayerName == null) return;

            _matchData.Events.Add(new GameEvent
            {
                Type = "FlashDetonate",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
        {
            { "ThrowerName", e.Player.PlayerName ?? "Unknown" },
            { "Position", new { X = e.X, Y = e.Y, Z = e.Z } }
        }
            });
        }

        private void OnPlayerBlind(Source1PlayerBlindEvent e)
        {
            if (e.Player == null || e.Attacker == null) return;

            string victimName = e.Player.PlayerName ?? "Unknown";
            _flashedPlayers.Add(victimName);
            _flashDurations[victimName] = e.BlindDuration;

            _matchData.Events.Add(new GameEvent
            {
                Type = "PlayerFlashed",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
                {
                    { "VictimName", victimName },
                    { "AttackerName", e.Attacker.PlayerName ?? "Unknown" },
                    { "FlashDuration", e.BlindDuration}
                }
            });
        }

        private void OnHEGrenadeDetonate(Source1HegrenadeDetonateEvent e)
        {
            if (e.Player?.PlayerName == null) return;  // Changed from e.Thrower

            _matchData.Events.Add(new GameEvent
            {
                Type = "HEGrenadeDetonate",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
        {
            { "ThrowerName", e.Player.PlayerName ?? "Unknown" },
            { "Position", new { X = e.X, Y = e.Y, Z = e.Z } }
        }
            });
        }

        private void OnMolotovDetonate(Source1MolotovDetonateEvent e)
        {
            if (e.Player?.PlayerName == null) return;  // Changed from e.Thrower

            _matchData.Events.Add(new GameEvent
            {
                Type = "MolotovDetonate",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
                {
                    { "ThrowerName", e.Player.PlayerName ?? "Unknown" },
                    { "Position", new { X = e.X, Y = e.Y, Z = e.Z } }
                }
            });
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

            _matchData.Events.Add(new GameEvent
            {
                Type = "PlayerDisconnect",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
                {
                    { "PlayerName", playerName }
                }
            });
        }

        // TODO: Fix this function, it's logic is just wrong in general
        // Needs to account for proximity and time
        private bool IsTradeKill(Source1PlayerDeathEvent deathEvent)
        {
            const int TRADE_WINDOW_TICKS = 128; // 2 seconds at 64 tick
            if (deathEvent.Player?.PlayerName == null) return false;

            var victimName = deathEvent.Player.PlayerName;
            if (!_lastDeathByPlayer.TryGetValue(victimName, out var lastDeath)) return false;

            return (_demo.CurrentDemoTick.Value - lastDeath.Tick) <= TRADE_WINDOW_TICKS;
        }

        private void OnRoundEnd(Source1RoundEndEvent e)
        {
            var roundEndData = new Dictionary<string, object>
            {
                { "Winner", e.Winner },
                { "Reason", e.Reason },
                { "Message", e.Message }
            };

            // Add final equipment values for all players
            foreach (var (playerName, _) in _playerInventory)
            {
                roundEndData[$"FinalEquipment_{playerName}"] = CalculatePlayerEquipmentValue(playerName);
                roundEndData[$"FinalMoney_{playerName}"] = _playerMoney.GetValueOrDefault(playerName, 0);
            }

            _matchData.Events.Add(new GameEvent
            {
                Type = "RoundEnd",
                Tick = _demo.CurrentDemoTick.Value,
                Data = roundEndData
            });
        }

        private void OnRoundStart(Source1RoundStartEvent e)
        {
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
                roundStartData[$"Team_{playerName}"] = team;

                if (!teamMoney.ContainsKey(team))
                    teamMoney[team] = 0;
                teamMoney[team] += money;
            }

            // Determine if it's a bonus round (one team has significantly more money)
            bool isBonus = false;
            if (teamMoney.Count == 2)
            {
                var moneyDiff = Math.Abs(teamMoney.ElementAt(0).Value - teamMoney.ElementAt(1).Value);
                isBonus = moneyDiff > 10000; // $10,000 difference threshold
            }

            roundStartData["IsBonus"] = isBonus;

            _matchData.Events.Add(new GameEvent
            {
                Type = "RoundStart",
                Tick = _demo.CurrentDemoTick.Value,
                Data = roundStartData
            });
        }

        private void OnPlayerDeath(Source1PlayerDeathEvent e)
        {
            if (e.Player == null || e.Attacker == null) return;

            string victimName = e.Player.PlayerName ?? "Unknown";
            string attackerName = e.Attacker.PlayerName ?? "Unknown";
            string victimSteamId = e.Player.SteamID.ToString();
            string attackerSteamId = e.Attacker.SteamID.ToString();

            UpdatePlayerStats(
                (long)e.Attacker.SteamID,
                (long)e.Player.SteamID,
                attackerName,
                victimName,
                attackerSteamId,
                victimSteamId,
                e.Weapon ?? "Unknown",
                e.Headshot);

            var deathEvent = new GameEvent
            {
                Type = "PlayerDeath",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
                {
                    { "KillerName", attackerName },
                    { "KillerSteamId", attackerSteamId },
                    { "KillerTeam", _playerTeams.GetValueOrDefault(attackerName, 0) },
                    { "VictimName", victimName },
                    { "VictimSteamId", victimSteamId },
                    { "VictimTeam", _playerTeams.GetValueOrDefault(victimName, 0) },
                    { "Weapon", e.Weapon ?? "Unknown" },
                    { "Headshot", e.Headshot },
                    { "IsTradeKill", IsTradeKill(e) },
                    { "WasFlashed", _flashedPlayers.Contains(victimName) &&
                                  _flashDurations.TryGetValue(victimName, out float flashDuration) &&
                                  flashDuration >= 0.7f }
                }
            };

            if (!string.IsNullOrEmpty(victimName))
            {
                _lastDeathByPlayer[victimName] = deathEvent;
            }

            _matchData.Events.Add(deathEvent);
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
            if (!_playerStats.ContainsKey(killerId))
            {
                _playerStats[killerId] = new PlayerStats
                {
                    Name = killerName,
                    SteamId = killerSteamId
                };
            }

            if (!_playerStats.ContainsKey(victimId))
            {
                _playerStats[victimId] = new PlayerStats
                {
                    Name = victimName,
                    SteamId = victimSteamId
                };
            }

            _playerStats[killerId].Kills++;
            _playerStats[victimId].Deaths++;

            if (!_playerStats[killerId].Data.ContainsKey("headshots"))
            {
                _playerStats[killerId].Data["headshots"] = 0;
            }
            if (headshot)
            {
                _playerStats[killerId].Data["headshots"] = (int)_playerStats[killerId].Data["headshots"] + 1;
            }

            _playerStats[killerId].HeadshotPercentage = _playerStats[killerId].Kills > 0 ?
                ((int)_playerStats[killerId].Data["headshots"]) * 100.0 / _playerStats[killerId].Kills : 0;

            var weaponStats = _playerStats[killerId].WeaponUsage
                .FirstOrDefault(w => w.WeaponName == weapon);

            if (weaponStats == null)
            {
                weaponStats = new WeaponStats { WeaponName = weapon };
                _playerStats[killerId].WeaponUsage.Add(weaponStats);
            }

            weaponStats.Kills++;

            // Track if it was a kill on a flashed player
            if (_flashedPlayers.Contains(victimName) &&
                _flashDurations.TryGetValue(victimName, out float flashDuration) &&
                flashDuration >= 0.7f)
            {
                if (!_playerStats[killerId].Data.ContainsKey("flash_kills"))
                {
                    _playerStats[killerId].Data["flash_kills"] = 0;
                }
                _playerStats[killerId].Data["flash_kills"] = (int)_playerStats[killerId].Data["flash_kills"] + 1;
            }
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

        private PlayerStats GetOrCreatePlayerStats(long steamId, string playerName)
        {
            if (!_playerStats.ContainsKey(steamId))
            {
                _playerStats[steamId] = new PlayerStats
                {
                    Name = playerName,
                    SteamId = steamId.ToString()
                };
            }
            return _playerStats[steamId];
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