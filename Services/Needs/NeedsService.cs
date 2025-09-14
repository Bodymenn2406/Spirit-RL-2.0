using GTANetworkAPI;
using Spirit.Core.Config;
using Spirit.Core.Entities; // SPlayer, Extensions
using Spirit.Core.Utils;    // Logger
using System.Collections.Concurrent;

namespace Spirit.Core.Services.Needs
{
    // Activity snapshot sent by client
    public class ActivityState
    {
        public bool onFoot { get; set; }
        public bool isSprinting { get; set; }
        public bool isSwimming { get; set; }
        public bool inVehicle { get; set; }
        public float speedKmh { get; set; }
        public bool afk { get; set; }
        public DateTime ts { get; set; } = DateTime.UtcNow;
    }

    public class PlayerNeedsState
    {
        public float Hunger;
        public float Thirst;
        public float DrainMult = 7f;
        public bool LowH, LowT, MidH, MidT, CritH, CritT;
        public bool BlockSprint;
        public DateTime LastHpTick = DateTime.MinValue;
        public DateTime LastUiPush = DateTime.MinValue;
        public DateTime SuppressToastsUntil = DateTime.MinValue; // z.B. 5s nach Connect
        public DateTime LastToastH = DateTime.MinValue;
        public DateTime LastToastT = DateTime.MinValue;
        public const int ToastCooldownMs = 10_000;
        public bool FirstEvalDone = false;
        public bool ZeroKillArmed { get; set; } = false; // prevent repeated kill

    }

    public static class NeedsService
    {
        private static readonly NeedsConfig Cfg = NeedsConfig.Default;

        private static readonly ConcurrentDictionary<int, ActivityState> _activity =
            new ConcurrentDictionary<int, ActivityState>(); // Player.Handle -> activity

        private static readonly ConcurrentDictionary<int, PlayerNeedsState> _needs =
            new ConcurrentDictionary<int, PlayerNeedsState>(); // Player.Handle -> needs

        // Füge Felder hinzu:
        private static CancellationTokenSource _cts;
        private static int _tickMs = 1000;
        private static DateTime _lastTick;


        private const string SdCritBlockSprint = "need:critBlockSprint";

        public static PlayerNeedsState GetOrCreateState(int handle, SPlayer sp)
        {
            return _needs.GetOrAdd(handle, _ => new PlayerNeedsState
            {
                Hunger = sp?.Hunger ?? 100f,
                Thirst = sp?.Thirst ?? 100f,
                DrainMult = 1f
            });
        }

        public static void StartLoop()
        {
            _cts = new CancellationTokenSource();
            _lastTick = DateTime.UtcNow;
            ScheduleNextTick();
            Logger.Log("[NeedsService] Loop started.", Logger.Level.Info);
        }

        public static void Stop()
        {
            _cts?.Cancel();
            Logger.Log("[NeedsService] Loop stopped.", Logger.Level.Info);
        }
        // NEU: kein Task.Run/await mehr – nur main-thread Ticks
        private static void ScheduleNextTick()
        {
            if (_cts?.IsCancellationRequested == true) return;

            NAPI.Task.Run(() =>
            {
                if (_cts?.IsCancellationRequested == true) return;

                try
                {
                    // dt berechnen
                    var now = DateTime.UtcNow;
                    var dt = (float)(now - _lastTick).TotalSeconds;
                    if (dt < 0f || dt > 5f) dt = 1f; // clamp für Pausen/Hitches
                    _lastTick = now;

                    // ==== TICK BODY (Mainthread – NAPI-Aufrufe safe) ====
                    foreach (var player in NAPI.Pools.GetAllPlayers())
                    {
                        if (!player.Exists) continue;

                        var sp = player.AsSPlayer();
                        if (sp == null || !sp.IsLoggedIn) continue;

                        // Activity holen
                        _activity.TryGetValue(player.Handle.Value, out var act);

                        // PlayerNeedsState früh holen/erzeugen (WICHTIG: vor effMult)
                        var st = _needs.GetOrAdd(player.Handle.Value, _ => new PlayerNeedsState
                        {
                            Hunger = Math.Clamp(sp.Hunger, 0f, 100f),
                            Thirst = Math.Clamp(sp.Thirst, 0f, 100f),
                            DrainMult = 1f,
                            SuppressToastsUntil = DateTime.UtcNow.AddSeconds(5)
                        });


                        // Aktivitätsmultiplikator bestimmen
                        float baseMult = NeedsConfig.Default.IdleMult;
                        if (act != null)
                        {
                            if (act.afk) baseMult = NeedsConfig.Default.AfkMult;
                            else if (act.isSwimming) baseMult = NeedsConfig.Default.SwimMult;
                            else if (act.isSprinting) baseMult = NeedsConfig.Default.SprintMult;
                            else if (act.inVehicle) baseMult = NeedsConfig.Default.InVehicleMult;
                            else baseMult = NeedsConfig.Default.WalkJogMult;
                        }

                        // Effektiver Multiplizierer inkl. Test-/Debug-Mult
                        var effMult = baseMult * st.DrainMult;

                        // Drain pro Sekunde
                        var hDelta = NeedsConfig.Default.BaseHungerPerMin * effMult * (dt / 60f);
                        var tDelta = NeedsConfig.Default.BaseThirstPerMin * effMult * (dt / 60f);

                        // Von aktuellem State abziehen (nicht von sp.Hunger, damit Konsum sofort berücksichtigt ist)
                        var newH = Math.Clamp(st.Hunger - hDelta, 0f, 100f);
                        var newT = Math.Clamp(st.Thirst - tDelta, 0f, 100f);

                        st.Hunger = newH;
                        st.Thirst = newT;

                        // Schwellen/Toasts/Debuffs
                        HandleThresholds(sp, st);

                        // Zurück in SPlayer schreiben → triggert bei dir SharedData + Dirty
                        sp.Hunger = st.Hunger;
                        sp.Thirst = st.Thirst;

                        // === inside your per-player tick body (after sp.Hunger / sp.Thirst updated) ===
                        if (sp.IsLoggedIn && player.Exists)
                        {
                            const float EPS = 0.001f; // tiny epsilon to treat ~0 as zero
                            bool zeroH = st.Hunger <= EPS;
                            bool zeroT = st.Thirst <= EPS;

                            if (zeroH && zeroT)
                            {
                                // Instant death when BOTH are zero (arm once to avoid spam)
                                if (!st.ZeroKillArmed)
                                {
                                    st.ZeroKillArmed = true;
                                    try { player.Health = 0; } catch { }
                                }
                            }
                            else
                            {
                                // Disarm when condition no longer true
                                if (st.ZeroKillArmed) st.ZeroKillArmed = false;

                                // Exactly one at zero -> periodic HP drain DOWN TO 0 (player can die)
                                if (zeroH ^ zeroT)
                                {
                                    if ((now - st.LastHpTick).TotalMilliseconds >= NeedsConfig.Default.HpTickIntervalMs)
                                    {
                                        st.LastHpTick = now;
                                        try
                                        {
                                            int dmg = NeedsConfig.Default.HpTickAmount; // reuse your config
                                                                                        // allow reaching 0 (death), not clamped at 1 anymore
                                            player.Health = Math.Max(0, player.Health - dmg);
                                        }
                                        catch { }
                                    }
                                }
                            }
                        }

                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"[NeedsService] Tick error: {ex}", Logger.Level.Error);
                }
                finally
                {
                    // nächsten Tick einplanen
                    if (_cts?.IsCancellationRequested != true)
                        ScheduleNextTick();
                }

            }, _tickMs); // Verzögerung vor Ausführung (ms)
        }


        // NeedsService.cs
        private static void HandleThresholds(SPlayer sp, PlayerNeedsState st)
        {
            var player = sp?.Base;
            if (player == null || !player.Exists) return;

            var cfg = NeedsConfig.Default;

            // ---- Flags (intern) aktualisieren wie gehabt ----
            bool lowH = st.Hunger < cfg.LowThreshold;
            bool midH = st.Hunger < cfg.MidThreshold;
            bool critH = st.Hunger < cfg.CriticalThreshold;

            bool lowT = st.Thirst < cfg.LowThreshold;
            bool midT = st.Thirst < cfg.MidThreshold;
            bool critT = st.Thirst < cfg.CriticalThreshold;

            st.LowH = lowH; st.MidH = midH; st.CritH = critH;
            st.LowT = lowT; st.MidT = midT; st.CritT = critT;

            // ---- Login-Gate: vor Login keine Toasts/kein Block ----
            if (!sp.IsLoggedIn)
            {
                if (st.BlockSprint) // sicherheitshalber freigeben
                {
                    st.BlockSprint = false;
                    player.SetSharedData("player:critBlockSprint", false);
                }
                return; // keine Toasts, kein Block vor Login
            }

            var walkThreshold = 25f; // passend zu THRESH_STYLE (Client)
            bool applyWalk = (st.Hunger < walkThreshold) || (st.Thirst < walkThreshold);

            // immer spiegeln (auch false zum Clearen), nur wenn eingeloggt
            player.SetSharedData("player:walkstyle:depressed", sp.IsLoggedIn && applyWalk);

            // ---- Sprint-Block immer nach Login spiegeln ----
            var shouldBlock = cfg.EnableSprintBlockOnCritical && (critH || critT);
            if (shouldBlock != st.BlockSprint)
            {
                st.BlockSprint = shouldBlock;
            }
            player.SetSharedData("player:critBlockSprint", shouldBlock);

            // ---- Toasts: nur Eskalation + kleiner Cooldown (optional) ----
            // (falls du schon Cooldown-Felder hast, kannst du die nutzen;
            //  hier minimalistisches Beispiel ohne Spam)
            if (!st.CritH && critH) sp.NotifyError("Dir wird schwarz vor Augen (Hunger)!");
            else if (!st.MidH && midH) sp.NotifyWarning("Du hast großen Hunger.");
            else if (!st.LowH && lowH) sp.NotifyInfo("Du hast Hunger.");

            if (!st.CritT && critT) sp.NotifyError("Du bist am Verdursten!");
            else if (!st.MidT && midT) sp.NotifyWarning("Du hast großen Durst.");
            else if (!st.LowT && lowT) sp.NotifyInfo("Du hast Durst.");
        }




        public static void SetDrainMultiplier(Player player, float multiplier, int? autoResetMs = null)
        {
            if (player == null || !player.Exists) return;
            var sp = player.AsSPlayer();
            var key = player.Handle.Value;

            var st = _needs.GetOrAdd(key, _ => new PlayerNeedsState
            {
                Hunger = sp?.Hunger ?? 100f,
                Thirst = sp?.Thirst ?? 100f,
                DrainMult = 1f,
                SuppressToastsUntil = DateTime.UtcNow.AddSeconds(5)
            });

            st.DrainMult = multiplier;
            player.SetSharedData("player:needDrainMult", multiplier);

            if (autoResetMs.HasValue && autoResetMs.Value > 0)
            {
                NAPI.Task.Run(() =>
                {
                    if (player != null && player.Exists)
                    {
                        var sp2 = player.AsSPlayer();
                        var st2 = _needs.GetOrAdd(player.Handle.Value, _ => new PlayerNeedsState
                        {
                            Hunger = sp2?.Hunger ?? 100f,
                            Thirst = sp2?.Thirst ?? 100f,
                            DrainMult = 1f,
                            SuppressToastsUntil = DateTime.UtcNow.AddSeconds(5)
                        });
                        st2.DrainMult = 1f;
                        player.SetSharedData("player:needDrainMult", 1f);
                        sp2?.NotifyInfo("Needs-Drain-Multiplikator automatisch zurückgesetzt (x1).");
                    }
                }, autoResetMs.Value);
            }
        }

        public static float GetDrainMultiplier(Player player)
        {
            if (player == null || !player.Exists) return 1f;
            return _needs.TryGetValue(player.Handle.Value, out var st) ? st.DrainMult : 1f;
        }


        // API used by events/commands
        public static void ApplyConsume(SPlayer sp, string itemId)
        {
            if (sp == null || sp.Base == null || !sp.Base.Exists) return;

            var key = sp.Base.Handle.Value;
            var needs = _needs.GetOrAdd(key, _ => new PlayerNeedsState { Hunger = 100f, Thirst = 100f });

            // Simple demo effects
            switch (itemId.ToLowerInvariant())
            {
                case "water":
                    needs.Thirst = Math.Min(100f, needs.Thirst + 45f);
                    sp.NotifyInfo("Du trinkst Wasser.");
                    break;
                case "soda":
                    needs.Thirst = Math.Min(100f, needs.Thirst + 35f);
                    needs.Hunger = Math.Min(100f, needs.Hunger + 5f);
                    sp.NotifyInfo("Du trinkst eine Limo.");
                    break;
                case "sandwich":
                    needs.Hunger = Math.Min(100f, needs.Hunger + 35f);
                    needs.Thirst = Math.Max(0f, needs.Thirst - 5f);
                    sp.NotifyInfo("Du isst ein Sandwich.");
                    break;
                case "burger":
                    needs.Hunger = Math.Min(100f, needs.Hunger + 50f);
                    needs.Thirst = Math.Max(0f, needs.Thirst - 10f);
                    sp.NotifyInfo("Du isst einen Burger.");
                    break;
                default:
                    sp.NotifyWarning("Unbekanntes Item.");
                    return;
            }

            sp.Hunger = needs.Hunger;
            sp.Thirst = needs.Thirst;
        }

        public static void SetInitialIfEmpty(SPlayer sp)
        {
            if (sp == null) return;

            // New player => ensure 100/100 at first load
            if (sp.Hunger <= 0 && sp.Thirst <= 0)
            {
                sp.Hunger = 100f;
                sp.Thirst = 100f;
            }
        }

        public static void OnRespawn(SPlayer sp)
        {
            if (sp == null) return;
            var key = sp.Base.Handle.Value;
            var needs = _needs.GetOrAdd(key, _ => new PlayerNeedsState());
            needs.Hunger = Math.Max(needs.Hunger, Cfg.RespawnHunger);
            needs.Thirst = Math.Max(needs.Thirst, Cfg.RespawnThirst);

            sp.Hunger = needs.Hunger;
            sp.Thirst = needs.Thirst;
            
        }

        public static void UpdateActivity(Player p, ActivityState a)
        {
            if (p == null || !p.Exists) return;
            _activity[p.Handle.Value] = a;
        }

        public static void Remove(Player p)
        {
            if (p == null) return;
            _activity.TryRemove(p.Handle.Value, out _);
            _needs.TryRemove(p.Handle.Value, out _);
        }

        public static ActivityState GetActivity(Player p)
        {
            _activity.TryGetValue(p.Handle.Value, out var a);
            return a;
        }
    }
}
