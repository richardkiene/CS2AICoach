using System.IO.Compression;
using CS2AICoach.Models;
using DemoFile;
using DemoFile.Game.Cs;

namespace CS2AICoach.Services
{
    public class DemoParser
    {
        private readonly string _demoFilePath;
        private readonly MatchData _matchData;
        private Dictionary<long, PlayerStats> _playerStats;
        private readonly CsDemoParser _demo;

        public DemoParser(string demoFilePath, CsDemoParser demo)
        {
            _demoFilePath = demoFilePath;
            _demo = demo;
            _matchData = new MatchData();
            _playerStats = new Dictionary<long, PlayerStats>();
        }

        public async Task<MatchData> ParseDemo()
        {
            _demo.PacketEvents.SvcServerInfo += OnServerInfo;
            _demo.Source1GameEvents.PlayerDeath += OnPlayerDeath;
            _demo.Source1GameEvents.WeaponFire += OnWeaponFire;
            _demo.Source1GameEvents.RoundStart += OnRoundStart;
            _demo.Source1GameEvents.RoundEnd += OnRoundEnd;

            byte[] demoBytes;
            using (var fileStream = File.OpenRead(_demoFilePath))
            {
                if (_demoFilePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    using var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress);
                    using var memoryStream = new MemoryStream();
                    await gzipStream.CopyToAsync(memoryStream);
                    demoBytes = memoryStream.ToArray();
                }
                else
                {
                    using var memoryStream = new MemoryStream();
                    await fileStream.CopyToAsync(memoryStream);
                    demoBytes = memoryStream.ToArray();
                }
            }

            using (var demoStream = new MemoryStream(demoBytes))
            {
                var reader = DemoFileReader.Create(_demo, demoStream);
                await reader.ReadAllAsync();
            }

            _matchData.PlayerStats = _playerStats;
            return _matchData;
        }

        private void OnServerInfo(CSVCMsg_ServerInfo e)
        {
            _matchData.MapName = e.MapName;
            _matchData.TickRate = e.TickInterval;
        }

        private void OnPlayerDeath(Source1PlayerDeathEvent e)
        {
            if (e.PlayerIndex.Value == default || e.AttackerIndex.Value == default) return;

            var victim = e.Player;
            var attacker = e.Attacker;

            if (victim != null && attacker != null)
            {
                UpdatePlayerStats(
                    e.AttackerIndex.Value,
                    e.PlayerIndex.Value,
                    attacker.PlayerName ?? "Unknown",
                    victim.PlayerName ?? "Unknown",
                    e.Weapon ?? "Unknown",
                    e.Headshot);

                _matchData.Events.Add(new GameEvent
                {
                    Type = "PlayerDeath",
                    Tick = _demo.CurrentDemoTick.Value,
                    Data = new Dictionary<string, object>
            {
                { "KillerName", attacker.PlayerName ?? "Unknown" },
                { "VictimName", victim.PlayerName ?? "Unknown" },
                { "Weapon", e.Weapon ?? "Unknown" },
                { "Headshot", e.Headshot }
            }
                });
            }
        }

        private void UpdatePlayerStats(long killerId, long victimId, string killerName, string victimName, string weapon, bool headshot)
        {
            if (!_playerStats.ContainsKey(killerId))
            {
                _playerStats[killerId] = new PlayerStats { Name = killerName };
            }

            if (!_playerStats.ContainsKey(victimId))
            {
                _playerStats[victimId] = new PlayerStats { Name = victimName };
            }

            _playerStats[killerId].Kills++;
            _playerStats[victimId].Deaths++;

            if (headshot)
            {
                var totalKills = _playerStats[killerId].Kills;
                _playerStats[killerId].HeadshotPercentage = (double)totalKills / 100;
            }

            var weaponStats = _playerStats[killerId].WeaponUsage
                .FirstOrDefault(w => w.WeaponName == weapon);

            if (weaponStats == null)
            {
                weaponStats = new WeaponStats { WeaponName = weapon };
                _playerStats[killerId].WeaponUsage.Add(weaponStats);
            }

            weaponStats.Kills++;
        }

        private void OnWeaponFire(Source1WeaponFireEvent e)
        {
            if (e.PlayerIndex.Value == default) return;

            var shooter = e.Player;
            if (shooter == null) return;

            if (!_playerStats.ContainsKey(e.PlayerIndex.Value))
            {
                _playerStats[e.PlayerIndex.Value] = new PlayerStats { Name = shooter.PlayerName ?? "Unknown" };
            }

            var weaponStats = _playerStats[e.PlayerIndex.Value].WeaponUsage
                .FirstOrDefault(w => w.WeaponName == e.Weapon);

            if (weaponStats == null)
            {
                weaponStats = new WeaponStats { WeaponName = e.Weapon ?? "Unknown" };
                _playerStats[e.PlayerIndex.Value].WeaponUsage.Add(weaponStats);
            }

            weaponStats.TotalShots++;
        }
        private void OnRoundStart(Source1RoundStartEvent e)
        {
            _matchData.Events.Add(new GameEvent
            {
                Type = "RoundStart",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>()
            });
        }

        private void OnRoundEnd(Source1RoundEndEvent e)
        {
            _matchData.Events.Add(new GameEvent
            {
                Type = "RoundEnd",
                Tick = _demo.CurrentDemoTick.Value,
                Data = new Dictionary<string, object>
        {
            { "Winner", e.Winner }
        }
            });
        }
    }
}