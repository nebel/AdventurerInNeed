using Dalamud.Configuration;
using ImGuiNET;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.Text;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Utility;
using Dalamud.Logging;

namespace AdventurerInNeed {
    public class RouletteConfig {
        public bool Enabled;
        public bool Tank;
        public bool Healer;
        public bool DPS;
        public bool RequireDailyBonus;
    }

    public class AdventurerInNeedConfig : IPluginConfiguration {
        [NonSerialized]
        private AdventurerInNeed plugin;

        public Dictionary<uint, RouletteConfig> Roulettes { get; set; } = new Dictionary<uint, RouletteConfig>();

#if DEBUG
        public bool AlwaysShowAlert { get; set; }
#endif

        public int Version { get; set; }
        public bool InGameAlert { get; set; }
        public XivChatType ChatType { get; set; } = XivChatType.SystemMessage;
        public bool ShowBonusIcon = true;
        public DailyBonusSetting RequireDailyBonus = DailyBonusSetting.Never;

        public void Init(AdventurerInNeed plugin) {
            this.plugin = plugin;
        }

        public void Save() {
            AdventurerInNeed.PluginInterface.SavePluginConfig(this);
        }

        public bool DrawConfigUI() {
            var drawConfig = true;

            var scale = ImGui.GetIO().FontGlobalScale;

            var modified = false;

            ImGui.SetNextWindowSize(new Vector2(400 * scale, 350), ImGuiCond.FirstUseEver);
            ImGui.SetNextWindowSizeConstraints(new Vector2(400 * scale, 350), new Vector2(600 * scale, 750));
            ImGui.Begin($"{plugin.Name} Config", ref drawConfig, ImGuiWindowFlags.NoCollapse);

#if DEBUG
            var alwaysShowAlert = AlwaysShowAlert;
            if (ImGui.Checkbox("Debug: Always Alert", ref alwaysShowAlert)) {
                AlwaysShowAlert = alwaysShowAlert;
                Save();
            }

            if (ImGui.Button("Debug: Print All Alerts")) {
                foreach (var r in plugin.RouletteList) {
                    if (r.ContentRouletteRoleBonus.Row > 0) {
                        try {
                            plugin.ShowAlert(r, Roulettes[r.RowId], PreferredRole.Tank);
                            plugin.ShowAlert(r, Roulettes[r.RowId], PreferredRole.Healer);
                            plugin.ShowAlert(r, Roulettes[r.RowId], PreferredRole.DPS);
                        } catch (Exception ex) {
                            PluginLog.LogError(ex.ToString());
                        }
                    }
                }
            }
#endif

            var inGameAlerts = InGameAlert;
            if (ImGui.Checkbox("Send alerts in game chat.", ref inGameAlerts)) {
                InGameAlert = inGameAlerts;
                Save();
            }

            ImGui.SameLine();
            ImGui.SetNextItemWidth(150);

            var selectedDetails = ChatType.GetDetails();

            if (ImGui.BeginCombo("###chatType", ChatType == XivChatType.None ? "Any" : (selectedDetails == null ? ChatType.ToString() : selectedDetails.FancyName))) {

                foreach (var chatType in ((XivChatType[]) Enum.GetValues(typeof(XivChatType)))) {

                    var details = chatType.GetDetails();

                    if (ImGui.Selectable(chatType == XivChatType.None ? "Any" : (details == null ? chatType.ToString() : details.FancyName), chatType == ChatType)) {
                        ChatType = chatType;
                        Save();
                    }

                    if (chatType == ChatType) ImGui.SetItemDefaultFocus();
                }

                ImGui.EndCombo();
            }

            var showBonusIcon = ShowBonusIcon;
            if (ImGui.Checkbox($"Show {SeIconChar.Buff.ToIconString()}  icon in alert if daily bonus is available.", ref showBonusIcon)) {
                ShowBonusIcon = showBonusIcon;
                Save();
            }

            ImGui.Text("Require daily bonus for alerts?");
            ImGuiHelpers.ScaledDummy(5f, 0);
            ImGui.SameLine();
            if (ImGui.RadioButton("Do not require daily bonus.", RequireDailyBonus == DailyBonusSetting.Never)) {
                RequireDailyBonus = DailyBonusSetting.Never;
                modified = true;
            }
            ImGuiHelpers.ScaledDummy(5f, 0);
            ImGui.SameLine();
            if (ImGui.RadioButton("Require daily bonus.", RequireDailyBonus == DailyBonusSetting.Always)) {
                RequireDailyBonus = DailyBonusSetting.Always;
                modified = true;
            }
            ImGuiHelpers.ScaledDummy(5f, 0);
            ImGui.SameLine();
            if (ImGui.RadioButton($"Require daily bonus only if {SeIconChar.Buff.ToIconString()}  column is checked.", RequireDailyBonus == DailyBonusSetting.UseRouletteConfig)) {
                RequireDailyBonus = DailyBonusSetting.UseRouletteConfig;
                modified = true;
            }

            ImGui.Separator();
            ImGui.Columns(7, "###cols", false);
            ImGui.SetColumnWidth(0, 40f * scale);
            ImGui.SetColumnWidth(1, ImGui.GetWindowWidth() - 280f * scale);
            ImGui.SetColumnWidth(2, 40f * scale);
            ImGui.SetColumnWidth(3, 40f * scale);
            ImGui.SetColumnWidth(4, 40f * scale);
            ImGui.SetColumnWidth(5, 40f * scale);
            ImGui.SetColumnWidth(6, 80f * scale);

            ImGui.NextColumn();
            ImGui.Text("Roulette");
            ImGui.NextColumn();
            ImGui.Text("T");
            ImGui.NextColumn();
            ImGui.Text("H");
            ImGui.NextColumn();
            ImGui.Text("D");
            ImGui.NextColumn();
            ImGui.Text(SeIconChar.Buff.ToIconString());
            ImGui.NextColumn();
            ImGui.Text("Current");
            ImGui.NextColumn();

            ImGui.Separator();

            if (plugin.RouletteList != null) {
                foreach (var r in plugin.RouletteList.Where(r => r != null && r.ContentRouletteRoleBonus != null && r.ContentRouletteRoleBonus.Row > 0)) {
                    var rCfg = Roulettes.ContainsKey(r.RowId) ? Roulettes[r.RowId] : new RouletteConfig();
                    modified = ImGui.Checkbox($"###rouletteEnabled{r.RowId}", ref rCfg.Enabled) || modified;
                    ImGui.NextColumn();

                    ImGui.Text(r.Name);
                    ImGui.NextColumn();
                    modified = ImGui.Checkbox($"###rouletteTankEnabled{r.RowId}", ref rCfg.Tank) || modified;
                    ImGui.NextColumn();
                    modified = ImGui.Checkbox($"###rouletteHealerEnabled{r.RowId}", ref rCfg.Healer) || modified;
                    ImGui.NextColumn();
                    modified = ImGui.Checkbox($"###rouletteDPSEnabled{r.RowId}", ref rCfg.DPS) || modified;
                    ImGui.NextColumn();

                    if (RequireDailyBonus != DailyBonusSetting.UseRouletteConfig) {
                        ImGui.PushStyleColor(ImGuiCol.CheckMark, ImGuiColors.ParsedGrey);
                    }
                    modified = ImGui.Checkbox($"###rouletteRequireBonus{r.RowId}", ref rCfg.RequireDailyBonus) || modified;
                    if (RequireDailyBonus != DailyBonusSetting.UseRouletteConfig) {
                        ImGui.PopStyleColor();
                    }
                    ImGui.NextColumn();

                    if (plugin.LastPreferredRoleList != null) {
                        var currentRole = plugin.LastPreferredRoleList.Get(r.ContentRouletteRoleBonus.Row);
                        ImGui.Text(currentRole.ToString());
                    }

                    ImGui.NextColumn();

                    Roulettes[r.RowId] = rCfg;
                }
            }
            
            ImGui.Columns(1);

            ImGui.End();

            if (modified) {
                Save();
            }

            return drawConfig;
        }
    }

    public enum DailyBonusSetting
    {
        Never,
        Always,
        UseRouletteConfig
    }
}
