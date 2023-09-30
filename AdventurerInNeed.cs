﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Dalamud.Game;
using Dalamud.Game.Text;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.IoC;
using Dalamud.Logging;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.Game.UI;
using Lumina.Excel.GeneratedSheets;

namespace AdventurerInNeed {
    public class AdventurerInNeed : IDalamudPlugin {
        public string Name => "Adventurer in Need";

        public AdventurerInNeedConfig PluginConfig { get; private set; }

        private bool drawConfigWindow;

        public List<ContentRoulette> RouletteList;

        private delegate IntPtr CfPreferredRoleChangeDelegate(IntPtr data);

        private Hook<CfPreferredRoleChangeDelegate> cfPreferredRoleChangeHook;

        internal PreferredRoleList LastPreferredRoleList;

        [PluginService] public static ISigScanner SigScanner { get; private set; } = null!;
        [PluginService] public static IDataManager Data { get; private set; } = null!;
        [PluginService] public static IChatGui ChatGui { get; private set; } = null!;
        [PluginService] public static ICommandManager CommandManager { get; private set; } = null!;
        [PluginService] public static IGameInteropProvider GameInteropProvider { get; private set; } = null!;
        [PluginService] public static DalamudPluginInterface PluginInterface { get; private set; } = null!;

        public void Dispose() {
            PluginInterface.UiBuilder.Draw -= this.BuildUI;
            cfPreferredRoleChangeHook?.Disable();
            cfPreferredRoleChangeHook?.Dispose();
            RemoveCommands();
        }

        public AdventurerInNeed() {
            this.PluginConfig = (AdventurerInNeedConfig) PluginInterface.GetPluginConfig() ?? new AdventurerInNeedConfig();
            this.PluginConfig.Init(this);

            var cfPreferredRolePtr = SigScanner.ScanText("E8 ?? ?? ?? ?? 48 8D 4B 70 E8 ?? ?? ?? ?? E9");

            if (cfPreferredRolePtr == IntPtr.Zero) {
                PluginLog.LogError("Failed to hook the cfPreferredRoleChange method.");
                return;
            }

#if DEBUG
            drawConfigWindow = true;
#endif

            PluginInterface.UiBuilder.OpenConfigUi += () => {
                this.drawConfigWindow = true;
            };

            RouletteList = Data.GetExcelSheet<ContentRoulette>().ToList();
            cfPreferredRoleChangeHook = GameInteropProvider.HookFromAddress(cfPreferredRolePtr, new CfPreferredRoleChangeDelegate(CfPreferredRoleChangeDetour));
            cfPreferredRoleChangeHook.Enable();
            PluginInterface.UiBuilder.Draw += this.BuildUI;

            SetupCommands();
        }

        private IntPtr CfPreferredRoleChangeDetour(IntPtr data) {
            UpdatePreferredRoleList(Marshal.PtrToStructure<PreferredRoleList>(data));
            return cfPreferredRoleChangeHook.Original(data);
        }

        private void UpdatePreferredRoleList(PreferredRoleList preferredRoleList) {
            var firstUpdate = LastPreferredRoleList == null;
#if DEBUG
            PluginLog.Log($"Updating Preferred Role List (firstUpdate={firstUpdate})");
#endif
            LastPreferredRoleList ??= preferredRoleList;

            foreach (var roulette in RouletteList.Where(roulette => roulette.ContentRouletteRoleBonus.Row != 0)) {
                try {
                    var rouletteConfig = PluginConfig.Roulettes[roulette.RowId];
                    if (!rouletteConfig.Enabled) continue;

                    var role = preferredRoleList.Get(roulette.ContentRouletteRoleBonus.Row);
                    var oldRole = LastPreferredRoleList.Get(roulette.ContentRouletteRoleBonus.Row);

#if DEBUG
                    PluginLog.Log($"{roulette.Name}: {oldRole} => {role}");

                    if (role != oldRole || firstUpdate || PluginConfig.AlwaysShowAlert) {
#else
                    if (role != oldRole || firstUpdate) {
#endif
                        if (PluginConfig.RequireDailyBonus == DailyBonusSetting.Never
                            || rouletteConfig.RequireDailyBonus == false
                            || HasBonus(roulette)) {
                            ShowAlert(roulette, rouletteConfig, role);
                        }
#if DEBUG
                        else {
                            PluginLog.Debug("Suppressed alert due to no bonus");
                        }
#endif
                    }

#if DEBUG
                } catch (Exception ex) {
                    PluginLog.LogError(ex.ToString());
#else
                } catch {
                    // Ignored
#endif
                }
            }

            LastPreferredRoleList = preferredRoleList;
        }

        internal void ShowAlert(ContentRoulette roulette, RouletteConfig config, PreferredRole role) {
            if (!config.Enabled) return;

            var doAlert = role switch {
                PreferredRole.Tank => config.Tank,
                PreferredRole.Healer => config.Healer,
                PreferredRole.DPS => config.DPS,
                _ => false
            };

            if (!doAlert) return;

            if (PluginConfig.InGameAlert) {
                ushort roleForegroundColor = role switch {
                    PreferredRole.Tank => 37,
                    PreferredRole.Healer => 504,
                    PreferredRole.DPS => 545,
                    _ => 0,
                };

                var icon = role switch {
                    PreferredRole.Tank => BitmapFontIcon.Tank,
                    PreferredRole.Healer => BitmapFontIcon.Healer,
                    PreferredRole.DPS => BitmapFontIcon.DPS,
                    _ => BitmapFontIcon.Warning
                };

                var payloads = new List<Payload> {
                    new UIForegroundPayload(500),
                    new TextPayload(roulette.Name),
                    new UIForegroundPayload(0),
                };

                if (PluginConfig.ShowBonusIcon && HasBonus(roulette)) {
                    payloads.AddRange(new Payload[] {
                        new UIForegroundPayload(568),
                        new TextPayload(SeIconChar.Buff.ToIconString()),
                        new UIForegroundPayload(0),
                    });
                }

                payloads.AddRange(new Payload[] {
                    new TextPayload(" needs a "),
                    new IconPayload(icon),
                    new UIForegroundPayload(roleForegroundColor),
                    new TextPayload(role.ToString()),
                    new UIForegroundPayload(0),
                    new TextPayload("."),
                });

                var seString = new SeString(payloads);

                var xivChat = new XivChatEntry() {
                    Message = seString
                };

                if (PluginConfig.ChatType != XivChatType.None) {
                    xivChat.Type = PluginConfig.ChatType;
                    xivChat.Name = this.Name;
                }

                ChatGui.Print(xivChat);
            }
        }

        public void SetupCommands() {
            CommandManager.AddHandler("/pbonus", new Dalamud.Game.Command.CommandInfo(OnConfigCommandHandler) {
                HelpMessage = $"Open config window for {this.Name}",
                ShowInHelp = true
            });
        }

        public void OnConfigCommandHandler(string command, string args) {
            drawConfigWindow = !drawConfigWindow;
        }

        public void RemoveCommands() {
            CommandManager.RemoveHandler("/pbonus");
        }

        private void BuildUI() {
            drawConfigWindow = drawConfigWindow && PluginConfig.DrawConfigUI();
        }

        internal static unsafe bool HasBonus(ContentRoulette roulette) {
            if (roulette.RowId > byte.MaxValue) return true;
            var rouletteController = RouletteController.Instance();
            return rouletteController->IsRouletteIncomplete((byte)roulette.RowId);
        }
    }
}
