﻿using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using Pk3DSRNGTool.Controls;
using Pk3DSRNGTool.RNG;
using Pk3DSRNGTool.Core;
using static PKHeX.Util;

namespace Pk3DSRNGTool
{
    public partial class MainForm : Form
    {
        #region global variables
        private string version = "0.9.0";

        private int Ver { get => Gameversion.SelectedIndex; set => Gameversion.SelectedIndex = value; }
        private Pokemon[] Pokemonlist;
        private Pokemon FormPM => RNGPool.PM;
        private byte Method => (byte)RNGMethod.SelectedIndex;
        private bool IsEvent => Method == 1;
        private bool IsPokemonLink => Method == 0 && ((FormPM as PKM6)?.PokemonLink ?? false);
        private bool IsHorde => Method == 2 && (FormPM as PKMW6)?.Type == EncounterType.Horde;
        private bool Gen6 => Ver < 4;
        private bool Gen7 => 4 <= Ver && Ver < 8;
        private bool gen6timeline => Gen6 && CreateTimeline.Checked && Gen6Tiny.Any(t => t > 0);
        private bool gen6timeline_available => Gen6 && (Method == 0 && !AlwaysSynced.Checked || Method == 2);
        private byte lastgen;
        private EncounterArea ea;
        private bool IsNight => Night.Checked;
        private int[] slotspecies => ea?.getSpecies(Ver, IsNight) ?? new int[0];
        private byte Modelnum => (byte)(NPC.Value + 1);
        private RNGFilters filter;
        private byte lastmethod;
        private ushort lasttableindex;
        private int timercounter;
        List<Frame> Frames = new List<Frame>();
        List<Frame_ID> IDFrames = new List<Frame_ID>();
        List<int> OtherTSVList = new List<int>();
        private static NtrClient ntrclient = new NtrClient();
        private static MTSeedFinder finder = new MTSeedFinder();
        #endregion

        public MainForm()
        {
            InitializeComponent();
        }

        #region Form Loading
        private void MainForm_Load(object sender, EventArgs e)
        {
            Type dgvtype = typeof(DataGridView);
            System.Reflection.PropertyInfo dgvPropertyInfo = dgvtype.GetProperty("DoubleBuffered", System.Reflection.BindingFlags.SetProperty
                 | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            dgvPropertyInfo.SetValue(DGV, true, null);
            dgvPropertyInfo.SetValue(DGV_ID, true, null);

            DGV.AutoGenerateColumns = false;
            DGV_ID.AutoGenerateColumns = false;

            IVInputer = new IVRange(this);

            Seed.Value = Properties.Settings.Default.Seed;
            var LastGameversion = Properties.Settings.Default.GameVersion;
            var LastPkm = Properties.Settings.Default.Poke;
            var LastCategory = Properties.Settings.Default.Category;
            var LastMethod = Properties.Settings.Default.Method;
            var _LastMethod = Properties.Settings.Default._Method;
            var Eggseed = Properties.Settings.Default.Key;
            Key0.Value = (uint)Eggseed;
            Key1.Value = Eggseed >> 32;
            ShinyCharm.Checked = Properties.Settings.Default.ShinyCharm;
            TSV.Value = Properties.Settings.Default.TSV;
            IP.Text = Properties.Settings.Default.IP;
            Loadlist(Properties.Settings.Default.TSVList);
            Advanced.Checked = Properties.Settings.Default.Advance;
            Status = new uint[] { Properties.Settings.Default.ST0, Properties.Settings.Default.ST1, Properties.Settings.Default.ST2, Properties.Settings.Default.ST3 };

            for (int i = 0; i < 6; i++)
                EventIV[i].Enabled = false;

            Gender.Items.AddRange(StringItem.genderstr);
            Ball.Items.AddRange(StringItem.genderstr);
            Event_Gender.Items.AddRange(StringItem.genderstr);
            Event_Nature.Items.AddRange(StringItem.naturestr);
            Wild_Nature.Items.AddRange(StringItem.naturestr);
            for (int i = 0; i <= StringItem.naturestr.Length; i++)
                SyncNature.Items.Add("");

            string l = Properties.Settings.Default.Language;
            int lang = Array.IndexOf(langlist, l);
            if (lang < 0) lang = Array.IndexOf(langlist, "en");

            lindex = lang;
            ChangeLanguage(null, null);

            Gender.SelectedIndex =
            Ball.SelectedIndex =
            Ability.SelectedIndex =
            SyncNature.SelectedIndex =
            Event_Species.SelectedIndex = Event_PIDType.SelectedIndex =
            Event_Ability.SelectedIndex = Event_Gender.SelectedIndex =
            M_ability.SelectedIndex = F_ability.SelectedIndex =
            M_Items.SelectedIndex = F_Items.SelectedIndex =
            Wild_Nature.SelectedIndex =
            0;
            Egg_GenderRatio.SelectedIndex = 1;

            Gameversion.SelectedIndex = LastGameversion;
            RNGMethod.SelectedIndex = _LastMethod;
            RNGMethod_Changed(null, null);
            CB_Category.SelectedIndex = LastCategory < CB_Category.Items.Count ? LastCategory : 0;
            Poke.SelectedIndex = LastPkm < Poke.Items.Count ? LastPkm : 0;
            RNGMethod.SelectedIndex = LastMethod;

            ByIVs.Checked = true;
            B_ResetFrame_Click(null, null);
            Advanced_CheckedChanged(null, null);
            ntrclient.Connected += OnConnected;
            finder.Update += UpdateProgressBar;
            finder.NewResult += UpdateDGV;
        }

        private void MainForm_Close(object sender, FormClosedEventArgs e)
        {
            Properties.Settings.Default.Save();
            ntrclient.disconnect();
            finder.Abort();
        }

        private void RefreshPKM()
        {
            if (Method != 0 && Method != 2) return;
            Pokemonlist = Pokemon.getSpecFormList(Ver, CB_Category.SelectedIndex, Method);
            var List = Pokemonlist.Select(s => new ComboItem(StringItem.Translate(s.ToString(), lindex), s.SpecForm)).ToList();
            Poke.DisplayMember = "Text";
            Poke.ValueMember = "Value";
            Poke.DataSource = new BindingSource(List, null);
            Poke.SelectedIndex = 0;
        }

        private void RefreshCategory()
        {
            Ver = Math.Max(Ver, 0);
            CB_Category.Items.Clear();
            var Category = Pokemon.getCategoryList(Ver, Method).Select(t => StringItem.Translate(t.ToString(), lindex)).ToArray();
            CB_Category.Items.AddRange(Category);
            CB_Category.SelectedIndex = 0;
            RefreshPKM();
        }

        private void RefreshLocation()
        {
            int[] locationlist = null;
            if (Gen6)
                locationlist = null;
            else if (Gen7)
                locationlist = FormPM.Conceptual ? LocationTable7.getSMLocation(CB_Category.SelectedIndex) : (FormPM as PKMW7)?.Location;

            MetLocation.Visible = SlotSpecies.Visible = Day.Visible = Night.Visible = L_Location.Visible = L_Slots.Visible = locationlist != null;
            if (locationlist == null)
                return;
            Locationlist = locationlist.Select(loc => new ComboItem(StringItem.getlocationstr(loc, Ver), loc)).ToList();

            MetLocation.DisplayMember = "Text";
            MetLocation.ValueMember = "Value";
            MetLocation.DataSource = new BindingSource(Locationlist, null);

            RefreshWildSpecies();
        }

        private void RefreshWildSpecies()
        {
            int tmp = SlotSpecies.SelectedIndex;
            var species = slotspecies;
            var List = Gen7 ? species.Skip(1).Distinct().Select(SpecForm => new ComboItem(StringItem.species[SpecForm & 0x7FF], SpecForm))
                : species.Distinct().Select(SpecForm => new ComboItem(StringItem.species[SpecForm & 0x7FF], SpecForm));
            List = new[] { new ComboItem("-", 0) }.Concat(List).ToList();
            SlotSpecies.DisplayMember = "Text";
            SlotSpecies.ValueMember = "Value";
            SlotSpecies.DataSource = new BindingSource(List, null);
            if (0 <= tmp && tmp < SlotSpecies.Items.Count)
                SlotSpecies.SelectedIndex = tmp;
        }

        private void LoadSlotSpeciesInfo()
        {
            int SpecForm = (int)SlotSpecies.SelectedValue;
            List<int> Slotidx = new List<int>();
            for (int i = Array.IndexOf(slotspecies, SpecForm); i > -1; i = Array.IndexOf(slotspecies, SpecForm, i + 1))
                Slotidx.Add(i);
            int offset = IsLinux ? 0 : 1;
            if (Gen6)
            {
                for (int i = 0; i < 12; i++)
                    Slot.CheckBoxItems[i + offset].Checked = Slotidx.Contains(i);
            }
            else
            {
                byte[] Slottype = EncounterArea7.SlotType[slotspecies[0]];
                for (int i = 0; i < 10; i++)
                    Slot.CheckBoxItems[i + offset].Checked = Slotidx.Contains(Slottype[i]);
            }

            SetPersonalInfo(SpecForm > 0 ? SpecForm : FormPM.SpecForm, skip: SlotSpecies.SelectedIndex != 0);
        }
        #endregion

        #region Basic UI

        private void VisibleTrigger(object sender, EventArgs e)
        {
            if ((sender as Control).Visible == false)
                (sender as CheckBox).Checked = false;
        }

        private void TabSelected(object sender, EventArgs e)
        {
            (sender as NumericUpDown)?.Select(0, Text.Length);
        }

        private void Status_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.ST0 = (uint)St0.Value;
            Properties.Settings.Default.ST1 = (uint)St1.Value;
            Properties.Settings.Default.ST2 = (uint)St2.Value;
            Properties.Settings.Default.ST3 = (uint)St3.Value;
        }

        private void Key_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.Key = (ulong)Key0.Value | ((ulong)Key1.Value << 32);
        }

        private void TSV_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.TSV = (short)TSV.Value;
        }

        private void ShinyCharm_CheckedChanged(object sender, EventArgs e)
        {
            MM_CheckedChanged(null, null);
            Properties.Settings.Default.ShinyCharm = ShinyCharm.Checked;
        }

        private void Advanced_CheckedChanged(object sender, EventArgs e)
        {
            B_GetTiny.Visible = B_BreakPoint.Visible = B_Resume.Visible = B_GetGen6Seed.Visible = Advanced.Checked;
            Properties.Settings.Default.Advance = Advanced.Checked;
        }

        private void Seed_ValueChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.Seed = Seed.Value;
            Properties.Settings.Default.Save();
        }

        private void Category_SelectedIndexChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.Category = (byte)CB_Category.SelectedIndex;
            RefreshPKM();
            SpecialOnly.Visible = Method == 2 && Gen7 && CB_Category.SelectedIndex > 0;
        }

        private void SearchMethod_CheckedChanged(object sender, EventArgs e)
        {
            IVPanel.Visible = ByIVs.Checked;
            StatPanel.Visible = ByStats.Checked;
            ShowStats.Enabled = ShowStats.Checked = ByStats.Checked;
        }

        private void RB_Gen6_CheckedChanged(object sender, EventArgs e)
        {
            WildPanel1.Visible = RB_1Wild.Checked;
            WildPanel2.Visible = RB_2Wild.Checked;
        }

        private void SyncNature_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (SyncNature.SelectedIndex > 0)
            {
                CompoundEyes.Checked = false;
                if (AlwaysSynced.Checked)
                    Nature.ClearSelection();
            }
            CompoundEyes.Enabled = SyncNature.SelectedIndex == 0;
        }

        private void Fix3v_CheckedChanged(object sender, EventArgs e)
        {
            PerfectIVs.Value = Fix3v.Checked ? 3 : 0;
        }

        private void Reset_Click(object sender, EventArgs e)
        {
            PerfectIVs.Value = Method == 0 && Fix3v.Checked ? 3 : 0;
            IVlow = new int[6];
            IVup = new[] { 31, 31, 31, 31, 31, 31 };
            Stats = new int[6];
            if (Method == 2)
                Filter_Lv.Value = 0;

            Nature.ClearSelection();
            HiddenPower.ClearSelection();
            Slot.ClearSelection();
            Ball.SelectedIndex = Gender.SelectedIndex = Ability.SelectedIndex = 0;

            IVInputer.Reset();

            BlinkFOnly.Checked = SafeFOnly.Checked = SpecialOnly.Checked =
            ShinyOnly.Checked = DisableFilters.Checked = false;
        }

        private void SetAsStarting_Click(object sender, EventArgs e)
        {
            try
            {
                var f = (int)DGV.CurrentRow.Cells["dgv_Frame"].Value;
                Frame_min.Value = f;
            }
            catch { }
        }

        private void B_SaveFilter_Click(object sender, EventArgs e)
        {
            SaveFileDialog saveFileDialog1 = new SaveFileDialog()
            {
                Filter = "txt files (*.txt)|*.txt",
                RestoreDirectory = true
            };
            if (saveFileDialog1.ShowDialog() == DialogResult.OK)
            {
                string backupfile = saveFileDialog1.FileName;
                if (backupfile != null)
                    System.IO.File.WriteAllLines(backupfile, FilterSettings.SettingString());
            }
        }

        private void B_LoadFilter_Click(object sender, EventArgs e)
        {
            try
            {
                OpenFileDialog OFD = new OpenFileDialog();
                DialogResult result = OFD.ShowDialog();
                if (result == DialogResult.OK)
                {
                    string file = OFD.FileName;
                    if (System.IO.File.Exists(file))
                    {
                        string[] list = System.IO.File.ReadAllLines(file);
                        int tmp;
                        Reset_Click(null, null);
                        foreach (string str in list)
                        {
                            string[] SplitString = str.Split(new[] { " = " }, StringSplitOptions.None);
                            if (SplitString.Length < 2)
                                continue;
                            string name = SplitString[0];
                            string value = SplitString[1];
                            switch (name)
                            {
                                case "Nature":
                                    var naturelist = value.Split(',').ToArray();
                                    for (int i = StringItem.naturestr.Length - 1; i >= 0; i--)
                                        if (naturelist.Contains(StringItem.naturestr[i]))
                                            Nature.CheckBoxItems[i + 1].Checked = true;
                                    break;
                                case "HiddenPower":
                                    var hplist = value.Split(',').ToArray();
                                    for (int i = StringItem.hpstr.Length - 2; i > 0; i--)
                                        if (hplist.Contains(StringItem.hpstr[i]))
                                            HiddenPower.CheckBoxItems[i].Checked = true;
                                    break;
                                case "ShinyOnly":
                                    ShinyOnly.Checked = value == "T" || value == "True";
                                    break;
                                case "Ability":
                                    tmp = Convert.ToInt32(value);
                                    Sta_Ability.SelectedIndex = 0 < tmp && tmp < 4 ? tmp : 0;
                                    break;
                                case "Gender":
                                    tmp = Convert.ToInt32(value);
                                    Gender.SelectedIndex = 0 < tmp && tmp < 3 ? tmp : 0;
                                    break;
                                case "IVup":
                                    IVup = value.Split(',').ToArray().Select(s => Convert.ToInt32(s)).ToArray();
                                    break;
                                case "IVlow":
                                    IVlow = value.Split(',').ToArray().Select(s => Convert.ToInt32(s)).ToArray();
                                    break;
                                case "Number of Perfect IVs":
                                    tmp = Convert.ToInt32(value);
                                    PerfectIVs.Value = 0 < tmp && tmp < 7 ? tmp : 0;
                                    break;
                            }
                        }
                    }
                }
            }
            catch
            {
                Error(FILEERRORSTR[lindex]);
            }
        }

        private void GameVersion_SelectedIndexChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.GameVersion = (byte)Gameversion.SelectedIndex;

            byte currentgen = (byte)(Gen6 ? 6 : 7);
            if (currentgen != lastgen)
            {
                var slotnum = new bool[Gen6 ? 12 : 10].Select((b, i) => (i + 1).ToString()).ToArray();
                Slot.Items.Clear();
                Slot.BlankText = "-";
                Slot.Items.AddRange(slotnum);
                Slot.CheckBoxItems[0].Checked = true;
                Slot.CheckBoxItems[0].Checked = false;

                Event_Species.Items.Clear();
                Event_Species.Items.AddRange(new string[] { "-" }.Concat(StringItem.species.Skip(1).Take(Gen6 ? 721 : 802)).ToArray());
                Event_Species.SelectedIndex = 0;

                lastgen = currentgen;
            }

            RNGMethod_Changed(null, null);
        }

        private void RNGMethod_Changed(object sender, EventArgs e)
        {
            Properties.Settings.Default.Method = Method;

            DGVToolTip.RemoveAll();

            if (Method < 6)
                RNGMethod.TabPages[Method].Controls.Add(this.RNGInfo);
            if (Method < 4)
                RNGMethod.TabPages[Method].Controls.Add(this.Filters);
            MainRNGEgg.Checked &= Method == 3;
            bool mainrngegg = Method == 3 && (MainRNGEgg.Checked || Gen6);
            RB_FrameRange.Checked = true;

            DGV.Visible = Method < 4;
            DGV_ID.Visible = Method == 4;

            // Contorls in RNGInfo
            AroundTarget.Visible = Method < 3 || mainrngegg;
            timedelaypanel.Visible = Method < 3 || mainrngegg || Method == 5;
            L_Correction.Visible = Correction.Visible = Gen7 && Method == 2; // Honey
            ConsiderDelay.Visible = Timedelay.Visible = label10.Visible = Method < 4; // not show in toolkit
            label10.Text = Gen7 ? "+4F" : "F";
            L_NPC.Visible = NPC.Visible = Gen7 || Method == 5; // not show in gen6
            RB_EggShortest.Visible =
            EggPanel.Visible = EggNumber.Visible = Method == 3 && !mainrngegg;
            CreateTimeline.Visible = TimeSpan.Visible = Gen7 && Method < 3 || MainRNGEgg.Checked || gen6timeline_available;
            GB_Tiny.Visible = gen6timeline_available && CreateTimeline.Checked;

            if (Method > 4)
                return;

            if (0 == Method || Method == 2)
            {
                int currmethod = (Method << 3) | Ver;
                if (lastmethod != currmethod)
                {
                    var poke = Poke.SelectedIndex;
                    var category = CB_Category.SelectedIndex;
                    RefreshCategory();
                    lastmethod = (byte)currmethod;
                    Properties.Settings.Default._Method = Method;
                    CB_Category.SelectedIndex = category < CB_Category.Items.Count ? category : 0;
                    Poke.SelectedIndex = poke < Poke.Items.Count ? poke : 0;
                }
                else if (Poke.Items.Count > 0 && sender != AlwaysSynced)
                    Poke_SelectedIndexChanged(null, null);
            }

            if (MainRNGEgg.Checked)
            {
                DGVToolTip.SetToolTip(L_NPC, "Tips: NPC can be 4-6");
                DGVToolTip.SetToolTip(NPC, "Tips: NPC can be 4-6");
            }

            SpecialOnly.Visible = Method == 2 && Gen7 && CB_Category.SelectedIndex > 0;
            L_Ball.Visible = Ball.Visible = Gen7 && Method == 3;
            L_Slot.Visible = Slot.Visible = Method == 2;
            ByIVs.Enabled = ByStats.Enabled = Method < 3;

            Gen6EggPanel.Visible = Gen6 && Method == 3;
            GB_Tiny.Visible &= Gen6;

            MT_SeedKey.Visible =
            Sta_AbilityLocked.Visible =
            RNGPanel.Visible = Gen6;
            B_IVInput.Visible = Gen7 && ByIVs.Checked;
            TinyMT_Status.Visible = Homogeneity.Visible =
            Lv_max.Visible = Lv_min.Visible = L_Lv.Visible = label9.Visible =
            GB_RNGGEN7ID.Visible =
            BlinkWhenSync.Visible =
            Filter_G7TID.Visible = Gen7;

            MM_CheckedChanged(null, null);
            NPC_ValueChanged(null, null);

            switch (Method)
            {
                case 0: Sta_Setting.Controls.Add(EnctrPanel); return;
                case 1: NPC.Value = 4; Event_CheckedChanged(null, null); return;
                case 2: Wild_Setting.Controls.Add(EnctrPanel); return;
                case 3: ByIVs.Checked = true; break;
                case 4: (Gen7 ? Filter_G7TID : Filter_TID).Checked = true; break;
            }
        }

        private void CreateTimeline_CheckedChanged(object sender, EventArgs e)
        {
            Frame_max.Visible = label7.Visible =
            ConsiderDelay.Enabled = !(L_StartingPoint.Visible = CreateTimeline.Checked);

            if (CreateTimeline.Checked) { ConsiderDelay.Checked = true; GB_Tiny.Visible = gen6timeline_available; };
            NPC_ValueChanged(null, null);
        }

        private void B_ResetFrame_Click(object sender, EventArgs e)
        {
            if (Gen7)
                Frame_min.Value = Method < 3 || MainRNGEgg.Checked ? 418 : Method == 4 ? 1012 : 0;
            else
                Frame_min.Value = 0;
            TargetFrame.Value = 5000;
            Frame_max.Value = 50000;
            if (0 == Method || Method == 2)
                Poke_SelectedIndexChanged(null, null);
        }

        private void NPC_ValueChanged(object sender, EventArgs e)
        {
            SafeFOnly.Visible = BlinkFOnly.Visible = false;
            if (Gen7 && !CreateTimeline.Checked && (Method < 3 || MainRNGEgg.Checked) )
                (NPC.Value == 0 ? BlinkFOnly : SafeFOnly).Visible = true;
        }

        // Wild RNG
        private void MetLocation_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (Gen7)
            {
                ea = LocationTable7.Table.FirstOrDefault(t => t.Locationidx == (int)MetLocation.SelectedValue);
                var tmp = ea as EncounterArea7;
                NPC.Value = tmp.NPC;
                Correction.Value = tmp.Correction;

                Lv_min.Value = ea.VersionDifference && Ver == 5 ? tmp.LevelMinMoon : tmp.LevelMin;
                Lv_max.Value = ea.VersionDifference && Ver == 5 ? tmp.LevelMaxMoon : tmp.LevelMax;
            }
            else
                ea = (Ver > 1 ? LocationTable6.Table_ORAS : null)?.FirstOrDefault(t => t.Locationidx == (int)MetLocation.SelectedValue);

            RefreshWildSpecies();
        }

        private void SlotSpecies_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (SlotSpecies.SelectedIndex > 0 && (Filter_Lv.Value > Lv_max.Value || Filter_Lv.Value < Lv_min.Value))
                Filter_Lv.Value = 0;
            LoadSlotSpeciesInfo();
        }

        private void Special_th_ValueChanged(object sender, EventArgs e)
        {
            L_Rate.Visible = Special_th.Visible = Special_th.Value > 0;
        }

        private void DayNight_CheckedChanged(object sender, EventArgs e)
        {
            if (ea.DayNightDifference)
                RefreshWildSpecies();
        }

        private void SetAsTarget_Click(object sender, EventArgs e)
        {
            try
            {
                TargetFrame.Value = Convert.ToDecimal(DGV.CurrentRow.Cells["dgv_Frame"].Value);
            }
            catch (NullReferenceException)
            {
                Error(NOSELECTION_STR[lindex]);
            }
        }

        private void B_IVInput_Click(object sender, EventArgs e)
        {
            IVInputer.ShowDialog();
        }

        private void Sta_AbilityLocked_CheckedChanged(object sender, EventArgs e)
        {
            Sta_Ability.Visible = Sta_AbilityLocked.Checked;
        }

        private void DGV_CellMouseEnter(object sender, DataGridViewCellEventArgs e)
        {
            if (Advanced.Checked)
                return;
            if (e.ColumnIndex < 0 || e.RowIndex < 0)
            {
                DGVToolTip.Hide(this);
                DGVToolTip.ToolTipTitle = null;
                return;
            }
            Rectangle cellRect = DGV.GetCellDisplayRectangle(e.ColumnIndex, e.RowIndex, false);
            if (DGV.Columns[e.ColumnIndex].Name == "dgv_mark" && !CreateTimeline.Checked)
            {
                DGVToolTip.ToolTipTitle = "Marks";
                DGVToolTip.Show("-: The safe frames can be 100% predicted.\r\n"
                    + "★: One person on the map will blink soon. Warning for following frames.\r\n"
                    + (Modelnum > 1 ? "?: The spread might be affected by the history of NPC blink status, it's unsafe."
                                    : "5: This frame will survive for 5/30 second.\r\n30: This frame will survive for 1.00 second.\r\n36: This frame will survive for 1.20 second.")
                    , this,
                    DGV.Location.X + cellRect.X + cellRect.Size.Width,
                    DGV.Location.Y + cellRect.Y + cellRect.Size.Height,
                    8000);
                return;
            }
            if (Gen6 && RNGPool.IsMainRNGEgg && (DGV.Columns[e.ColumnIndex].Name == "dgv_psv" || DGV.Columns[e.ColumnIndex].Name == "dgv_pid"))
            {
                DGVToolTip.ToolTipTitle = "Tips";
                DGVToolTip.Show("This column shows the main RNG PID/ESV of the current egg (w/o mm or sc)\r\nNot the part of spread prediction of the egg seed in the same row."
                    , this,
                    DGV.Location.X + cellRect.X + cellRect.Size.Width,
                    DGV.Location.Y + cellRect.Y + cellRect.Size.Height,
                    8000);
                return;
            }
            if (Gen7 && DGV.Columns[e.ColumnIndex].Name == "dgv_shift")
            {
                DGVToolTip.ToolTipTitle = "Frame Shift for Eontimer Calibration";
                DGVToolTip.Show("This column shows frame to time conversion, i.e. 1F = 1/60 sec."
                    , this,
                    DGV.Location.X + cellRect.X + cellRect.Size.Width,
                    DGV.Location.Y + cellRect.Y + cellRect.Size.Height,
                    3000);
                return;
            }
            if (DGV.Columns[e.ColumnIndex].Name == "dgv_adv")
            {
                DGVToolTip.ToolTipTitle = "Frame Advance";
                DGVToolTip.Show(EggPanel.Visible ? RB_EggShortest.Checked ? "To reach target frame, please precisely follow the listed procedure" : "By receiving this egg." : "By recieving this Pokemon."
                    , this,
                    DGV.Location.X + cellRect.X + cellRect.Size.Width,
                    DGV.Location.Y + cellRect.Y + cellRect.Size.Height,
                    8000);
                return;
            }
            DGVToolTip.Hide(this);
        }
        #endregion

        #region DataEntry

        private void SetPersonalInfo(int Species, int Forme, bool skip = false)
        {
            SyncNature.Enabled = !(FormPM?.Nature < 25) && FormPM.Syncable;

            // Load from personal table
            var t = Gen6 ? PersonalTable.ORAS.getFormeEntry(Species, Forme) : PersonalTable.SM.getFormeEntry(Species, Forme);
            BS = new[] { t.HP, t.ATK, t.DEF, t.SPA, t.SPD, t.SPE };
            GenderRatio.SelectedValue = t.Gender;
            Fix3v.Checked = t.EggGroups[0] == 0x0F; //Undiscovered Group

            // Load from Pokemonlist
            if (FormPM == null || IsEvent || skip)
                return;
            Filter_Lv.Value = FormPM.Level;
            AlwaysSynced.Checked = FormPM.AlwaysSync;
            ShinyLocked.Checked = FormPM.ShinyLocked;
            GenderRatio.SelectedValue = (int)FormPM.GenderRatio;
            AlwaysSynced.Text = SYNC_STR[lindex, FormPM.Syncable && FormPM.Nature > 25 ? 0 : 1];
            if (!FormPM.Syncable)
                SyncNature.SelectedIndex = 0;
            if (FormPM.Nature < 25)
                SyncNature.SelectedIndex = FormPM.Nature + 1;
            Fix3v.Checked &= !FormPM.Egg;
            Timedelay.Value = FormPM.Delay;

            if (FormPM is PKM7 pm7)
            {
                NPC.Value = pm7.NPC;
                BlinkWhenSync.Checked = !(pm7.AlwaysSync || pm7.NoBlink);
                return;
            }
            if (FormPM is PKM6 pm6)
                if (Sta_AbilityLocked.Checked = pm6.Ability > 0)
                    Sta_Ability.SelectedIndex = pm6.Ability >> 1; // 1/2/4 -> 0/1/2
            FirstEncounter.Visible = L_WildIVsCnt.Visible = WildIVsCnt.Visible = (FormPM is PKMW6 pmw6 && pmw6.Type == EncounterType.PokeRadar);
        }

        private void SetPersonalInfo(int SpecForm, bool skip = false) => SetPersonalInfo(SpecForm & 0x7FF, SpecForm >> 11, skip);

        private void Poke_SelectedIndexChanged(object sender, EventArgs e)
        {
            Properties.Settings.Default.Poke = (byte)Poke.SelectedIndex;
            Filter_Lv.Value = 0;

            int specform = (int)(Poke.SelectedValue);
            RNGPool.PM = Pokemonlist[Poke.SelectedIndex];
            SetPersonalInfo(specform);
            GenderRatio.Enabled = FormPM.Conceptual;
            if (Method == 2)
            {
                RefreshLocation();
                if (FormPM is PKMW7 pmw7) // For UB
                {
                    Special_th.Value = pmw7.Rate?[MetLocation.SelectedIndex] ?? (byte)(CB_Category.SelectedIndex == 2 ? 50 : 0);
                    Correction.Enabled = Special_th.Enabled = pmw7.Conceptual;
                }
                else if (FormPM is PKMW6 pmw6)
                {
                    Special_th.Value = 0;
                    GB_Tiny.Visible = true;
                }
                return;
            }
            switch (specform)
            {
                case 382:
                case 383:
                    DGVToolTip.SetToolTip(Timedelay, "Tips: The delay varies from 2700-4000, depends on save and console");
                    DGVToolTip.SetToolTip(ConsiderDelay, "Tips: The delay varies from 2700-4000, depends on save and console"); break; // Grondon / Kyogre
                case 791:
                case 792:
                    DGVToolTip.SetToolTip(L_NPC, "Tips: NPC can be 2 or 6, it depends on save");
                    DGVToolTip.SetToolTip(NPC, "Tips: NPC can be 2 or 6, it depends on save"); break; // SolLuna
                case 801:
                    DGVToolTip.SetToolTip(L_NPC, "Tips: NPC can be 6 or 7. Depends on the person walking by");
                    DGVToolTip.SetToolTip(NPC, "Tips: NPC can be 6 or 7. Depends on the person walking by"); break; // Magearna
                default: DGVToolTip.RemoveAll(); break;
            }

            Sta_AbilityLocked.Enabled = Sta_Ability.Enabled =
            BlinkWhenSync.Enabled = AlwaysSynced.Enabled =
            ShinyLocked.Enabled = Fix3v.Enabled = FormPM.Conceptual;
        }
        #endregion

        #region UI communication
        private void getsetting(IRNG rng)
        {
            DGV.CellFormatting -= new DataGridViewCellFormattingEventHandler(DGV_CellFormatting); //Stop Freshing
            Frames.Clear();
            Frames = new List<Frame>();

            filter = FilterSettings;
            RNGPool.igenerator = getGenerator(Method);
            RNGPool.IsMainRNGEgg = MainRNGEgg.Checked || Gen6 && !ShinyCharm.Checked && !MM.Checked && RB_Accept.Checked;

            if (MainRNGEgg.Checked) // Get first egg
            {
                TinyMT tmt = new TinyMT(Status);
                RNGPool.CreateBuffer(50, tmt);
                RNGPool.firstegg = RNGPool.GenerateEgg7() as EggResult;
                RNGPool.igenerator = getStaSettings();
                (RNGPool.igenerator as Stationary7).ConsiderOtherTSV = ConsiderOtherTSV.Checked;
                (RNGPool.igenerator as Stationary7).OtherTSVs = OtherTSVList.ToArray();
            }

            Frame.showstats = ShowStats.Checked;
            int buffersize = 150;
            if (Gen7)
            {
                RNGPool.modelnumber = Modelnum;
                RNGPool.IsSolgaleo = Method == 0 && FormPM.Species == 791;
                RNGPool.IsLunala = Method == 0 && FormPM.Species == 792;
                RNGPool.IsExeggutor = Method == 0 && FormPM.Species == 103;
                RNGPool.DelayTime = (int)Timedelay.Value / 2;
                RNGPool.raining = ModelStatus.raining = Method == 2 && ea.Location == 120;
                RNGPool.PreHoneyCorrection = (int)Correction.Value;

                if (Method == 2)
                {
                    Frame.SpecialSlotStr = StringItem.gen7wildtypestr[CB_Category.SelectedIndex];
                    buffersize += RNGPool.modelnumber * 100;
                }
                if (RNGPool.Considerdelay = ConsiderDelay.Checked)
                    buffersize += RNGPool.modelnumber * RNGPool.DelayTime;
                if (Method == 3 && !MainRNGEgg.Checked)
                    buffersize = 100;
                if (Method < 3 || MainRNGEgg.Checked)
                    Frame.standard = CalcFrame((int)(AroundTarget.Checked ? TargetFrame.Value - 100 : Frame_min.Value), (int)TargetFrame.Value)[0] * 2;
            }
            if (Gen6)
            {
                switch (Method)
                {
                    case 1: buffersize = 80; break;
                    case 3: buffersize = 4; Egg6.MainRNGPID = null; break;
                }
                RNGPool.DelayTime = (int)Timedelay.Value;
                if (RNGPool.Considerdelay = ConsiderDelay.Checked)
                    buffersize += RNGPool.DelayTime;
                Frame.standard = (int)TargetFrame.Value - (int)(AroundTarget.Checked ? TargetFrame.Value - 100 : Frame_min.Value);
            }
            RNGPool.CreateBuffer(buffersize, rng);
        }

        private IGenerator getGenerator(byte method)
        {
            switch (method)
            {
                case 0: return getStaSettings();
                case 1: return getEventSetting();
                case 2: return getWildSetting();
                case 3: return getEggRNG();
                default: return null;
            }
        }

        private RNGFilters FilterSettings => new RNGFilters
        {
            Nature = Nature.CheckBoxItems.Skip(1).Select(e => e.Checked).ToArray(),
            HPType = HiddenPower.CheckBoxItems.Skip(1).Select(e => e.Checked).ToArray(),
            Gender = (byte)Gender.SelectedIndex,
            Ability = (byte)Ability.SelectedIndex,
            IVlow = IVlow,
            IVup = IVup,
            BS = ByStats.Checked ? BS : null,
            Stats = ByStats.Checked ? Stats : null,
            ShinyOnly = ShinyOnly.Checked,
            Skip = DisableFilters.Checked,
            PerfectIVs = (byte)PerfectIVs.Value,

            Level = (byte)Filter_Lv.Value,
            Slot = new bool[IsLinux ? 1 : 0].Concat(Slot.CheckBoxItems.Select(e => e.Checked)).ToArray(),
            SpecialOnly = SpecialOnly.Checked,

            Ball = (byte)Ball.SelectedIndex,
        };

        private IDFilters getIDFilter()
        {
            IDFilters f = new IDFilters();
            if (Filter_SID.Checked) f.IDType = 1;
            else if (Filter_G7TID.Checked) f.IDType = 2;
            f.Skip = ID_Disable.Checked;
            f.RE = ID_RE.Checked;
            f.IDList = ID_List.Lines;
            f.TSVList = TSV_List.Lines;
            f.RandList = RandList.Lines;
            return f;
        }

        private StationaryRNG getStaSettings()
        {
            StationaryRNG setting = Gen6 ? new Stationary6() : (StationaryRNG)new Stationary7();
            setting.Synchro_Stat = (byte)(SyncNature.SelectedIndex - 1);
            setting.TSV = (int)TSV.Value;
            setting.ShinyCharm = ShinyCharm.Checked;

            if (MainRNGEgg.Checked)
                return setting;

            // Load from template
            if (!FormPM.Conceptual)
            {
                setting.UseTemplate(RNGPool.PM);
                return setting;
            }

            // Load from UI
            int gender = (int)GenderRatio.SelectedValue;
            setting.IV3 = Fix3v.Checked;
            setting.Gender = FuncUtil.getGenderRatio(gender);
            setting.RandomGender = FuncUtil.IsRandomGender(gender);
            setting.AlwaysSync = AlwaysSynced.Checked;
            setting.Level = (byte)Filter_Lv.Value;
            setting.IsShinyLocked = ShinyLocked.Checked;
            setting.IVs = new int[] { -1, -1, -1, -1, -1, -1 };

            if (setting is Stationary7 setting7)
                setting7.blinkwhensync = BlinkWhenSync.Checked;
            if (setting is Stationary6 setting6)
                setting6.Ability = (byte)(Sta_AbilityLocked.Checked ? Sta_Ability.SelectedIndex + 1 : 0);

            return setting;
        }

        private EventRNG getEventSetting()
        {
            int[] IVs = { -1, -1, -1, -1, -1, -1 };
            for (int i = 0; i < 6; i++)
                if (EventIVLocked[i].Checked)
                    IVs[i] = (int)EventIV[i].Value;
            if (IVsCount.Value > 0 && IVs.Count(iv => iv >= 0) + IVsCount.Value > 5)
            {
                Error(SETTINGERROR_STR[lindex] + L_IVsCount.Text);
                IVs = new[] { -1, -1, -1, -1, -1, -1 };
            }
            EventRNG e = Gen6 ? (EventRNG)new Event6() : new Event7();
            if (e is Event6 e6)
                e6.IsORAS = Ver > 1;
            e.Species = (short)Event_Species.SelectedIndex;
            e.Forme = (byte)Event_Forme.SelectedIndex;
            e.Level = (byte)Filter_Lv.Value;
            e.IVs = (int[])IVs.Clone();
            e.IVsCount = (byte)IVsCount.Value;
            e.YourID = YourID.Checked;
            e.PIDType = (byte)Event_PIDType.SelectedIndex;
            e.AbilityLocked = AbilityLocked.Checked;
            e.NatureLocked = NatureLocked.Checked;
            e.GenderLocked = GenderLocked.Checked;
            e.OtherInfo = OtherInfo.Checked;
            e.EC = (uint)Event_EC.Value;
            e.Ability = (byte)Event_Ability.SelectedIndex;
            e.Nature = (byte)Event_Nature.SelectedIndex;
            e.Gender = (byte)Event_Gender.SelectedIndex;
            e.IsEgg = IsEgg.Checked;
            if (e.YourID)
                e.TSV = (ushort)TSV.Value;
            else
            {
                e.TID = (ushort)Event_TID.Value;
                e.SID = (ushort)Event_SID.Value;
                e.TSV = (ushort)((e.TID ^ e.SID) >> 4);
                e.PID = (uint)Event_PID.Value;
            }
            e.GetGenderSetting();
            return e;
        }

        private WildRNG getWildSetting()
        {
            WildRNG setting = Gen6 ? new Wild6() : (WildRNG)new Wild7();
            setting.Synchro_Stat = (byte)(SyncNature.SelectedIndex - 1);
            setting.TSV = (int)TSV.Value;
            setting.ShinyCharm = ShinyCharm.Checked;

            int slottype = 0;
            if (setting is Wild7 setting7)
            {
                if (ea.Locationidx == 1190) slottype = 1; // Poni Plains -4
                setting7.Levelmin = (byte)Lv_min.Value;
                setting7.Levelmax = (byte)Lv_max.Value;
                setting7.SpecialEnctr = (byte)Special_th.Value;
                setting7.UB = CB_Category.SelectedIndex == 1;
                setting7.SpecForm = new int[11];
                setting7.CompoundEye = CompoundEyes.Checked;
                for (int i = 1; i < 11; i++)
                    setting7.SpecForm[i] = slotspecies[EncounterArea7.SlotType[slotspecies[0]][i - 1]];
                if (setting7.SpecialEnctr > 0)
                {
                    setting7.SpecForm[0] = FormPM.SpecForm;
                    setting7.SpecialLevel = FormPM.Level;
                }
            }
            if (setting is Wild6 setting6)
            {
                if (FormPM is PKMW6 pmw6)
                {
                    if (pmw6.Conceptual)
                        setting6.BlankGenderRatio = (int)GenderRatio.SelectedValue;
                    switch (pmw6.Type)
                    {
                        case EncounterType.Horde:
                            setting6.SpecForm = new int[6];
                            setting6.SlotLevel = new byte[6];
                            for (int i = 1; i < 6; i++)
                            {
                                setting6.SpecForm[i] = FormPM.SpecForm;
                                setting6.SlotLevel[i] = FormPM.Level;
                            }
                            break;
                        case EncounterType.PokeRadar:
                            setting6.IsShinyLocked = !FirstEncounter.Checked;
                            setting6._ivcnt = (int)WildIVsCnt.Value;
                            setting6.SpecForm = new[] { 0, 0 };
                            setting6.SlotLevel = new byte[] { 0, (byte)Filter_Lv.Value };
                            break;
                        case EncounterType.FriendSafari:
                            setting6._ivcnt = 2;
                            setting6._PIDroll_count = 4;
                            setting6.SpecForm = new[] { 0, 0 };
                            setting6.SlotLevel = new byte[] { 0, (byte)Filter_Lv.Value };
                            break;
                        case EncounterType.SingleSlot:
                            setting6.SpecForm = new[] { 0, FormPM.SpecForm };
                            setting6.SlotLevel = new byte[] { 0, FormPM.Level };
                            break;
                        default:
                            var area = ea as EncounterArea6;
                            setting6.SpecForm = new int[13];
                            setting6.SlotLevel = new byte[13];
                            for (int i = 1; i < 13; i++)
                            {
                                setting6.SpecForm[i] = slotspecies[i - 1];
                                setting6.SlotLevel[i] = area.Level[i - 1];
                            }
                            slottype = 2;
                            break;
                    };
                }
            }

            setting.Markslots();
            setting.SlotSplitter = WildRNG.SlotDistribution[slottype];

            return setting;
        }

        private EggRNG getEggRNG()
        {
            var setting = Gen6 ? new Egg6() : (EggRNG)new Egg7();
            setting.FemaleIVs = IV_Female;
            setting.MaleIVs = IV_Male;
            setting.MaleItem = (byte)M_Items.SelectedIndex;
            setting.FemaleItem = (byte)F_Items.SelectedIndex;
            setting.ShinyCharm = ShinyCharm.Checked;
            setting.TSV = (ushort)TSV.Value;
            setting.Gender = FuncUtil.getGenderRatio((int)Egg_GenderRatio.SelectedValue);
            if (setting is Egg7 setting7)
            {
                setting7.Homogeneous = Homogeneity.Checked;
                setting7.FemaleIsDitto = F_ditto.Checked;
            }
            setting.InheritAbility = (byte)(F_ditto.Checked ? M_ability.SelectedIndex : F_ability.SelectedIndex);
            setting.MMethod = MM.Checked;
            setting.NidoType = NidoType.Checked;

            setting.ConsiderOtherTSV = ConsiderOtherTSV.Checked && (ShinyCharm.Checked || MM.Checked || Gen6 && RB_Accept.Checked);
            setting.OtherTSVs = OtherTSVList.ToArray();

            setting.MarkItem();
            return setting;
        }
        #endregion

        #region Start Calculation
        private void AdjustDGVColumns()
        {
            if (Method == 4)
            {
                dgv_ID_rand64.Visible = dgv_clock.Visible = dgv_gen7ID.Visible = Gen7;
                dgv_ID_Mod100.Visible = dgv_ID_state.Visible = dgv_ID_rand.Visible = Gen6;
                dgv_ID_rand.Visible &= Advanced.Checked;
                DGV_ID.DataSource = IDFrames;
                DGV_ID.Refresh();
                DGV_ID.CurrentCell = null;
                if (IDFrames.Count > 0) DGV_ID.FirstDisplayedScrollingRowIndex = 0;
                return;
            }
            dgv_synced.Visible = Method < 3 && FormPM.Syncable && !IsEvent;
            dgv_item.Visible = dgv_Lv.Visible = dgv_slot.Visible = Method == 2;
            dgv_rand.Visible = Gen6 || Gen7 && Method == 3 && !MainRNGEgg.Checked;
            dgv_rand.Visible &= Advanced.Checked;
            dgv_state.Visible = Gen6 && Method < 4;
            SetAsCurrent.Visible =
            dgv_tinystate.Visible = Gen6 && (Method == 0 || Method == 2) && gen6timeline || Gen7 && Method == 3 && !MainRNGEgg.Checked;
            if (Gen6 && Method == 3) SetAsCurrent.Visible = true;
            dgv_tinystate.HeaderText = COLUMN_STR[lindex][Gen7 ? 1 : 2];
            SetAsAfter.Visible = Gen7 && Method == 3 && !MainRNGEgg.Checked;
            dgv_ball.Visible = Gen7 && Method == 3;
            dgv_adv.Visible = Gen7 && Method == 3 && !MainRNGEgg.Checked || IsPokemonLink;
            dgv_shift.Visible = dgv_time.Visible = !IsPokemonLink && (Gen6 || Method < 3 || MainRNGEgg.Checked);
            dgv_delay.Visible = dgv_mark.Visible = dgv_rand64.Visible = Gen7 && Method < 3 || MainRNGEgg.Checked;
            dgv_rand64.Visible |= Gen6 && Method == 3;
            dgv_rand64.HeaderText = COLUMN_STR[lindex][Gen6 ? 1 : 0];
            dgv_eggnum.Visible = EggNumber.Checked || RB_EggShortest.Checked;
            dgv_pid.Visible = dgv_psv.Visible = Method < 3 || ShinyCharm.Checked || MM.Checked || MainRNGEgg.Checked || Gen6 && RB_Accept.Checked;
            dgv_pid.Visible &= dgv_EC.Visible = Advanced.Checked;
            DGV.DataSource = Frames;
            DGV.CellFormatting += new DataGridViewCellFormattingEventHandler(DGV_CellFormatting);
            DGV.CurrentCell = null;
            if (Frames.Count > 0) DGV.FirstDisplayedScrollingRowIndex = 0;
        }

        private void Search_Click(object sender, EventArgs e)
        {
            if (Method == 5) // Gen7 ToolKit
            {
                CalcTime(null, null);
                return;
            }
            if (ivmin0.Value > ivmax0.Value)
                Error(SETTINGERROR_STR[lindex] + L_H.Text);
            else if (ivmin1.Value > ivmax1.Value)
                Error(SETTINGERROR_STR[lindex] + L_A.Text);
            else if (ivmin2.Value > ivmax2.Value)
                Error(SETTINGERROR_STR[lindex] + L_B.Text);
            else if (ivmin3.Value > ivmax3.Value)
                Error(SETTINGERROR_STR[lindex] + L_C.Text);
            else if (ivmin4.Value > ivmax4.Value)
                Error(SETTINGERROR_STR[lindex] + L_D.Text);
            else if (ivmin5.Value > ivmax5.Value)
                Error(SETTINGERROR_STR[lindex] + L_S.Text);
            else if (Frame_min.Value > Frame_max.Value)
                Error(SETTINGERROR_STR[lindex] + RB_FrameRange.Text);
            else
            {
                if (Gen6)
                    Search6();
                else
                    Search7();
                AdjustDGVColumns();
            }
            RNGPool.Clear();
            GC.Collect();
        }

        private static Font BoldFont = new Font("Microsoft Sans Serif", 8, FontStyle.Bold);
        private void DGV_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            int index = e.RowIndex;
            if (Frames.Count <= index || Frames[index].Formatted)
                return;
            var result = Frames[index].rt;
            var row = DGV.Rows[index];

            if (result.Shiny)
                row.DefaultCellStyle.BackColor = Color.LightCyan;
            if (Gen6 && Method == 3)
            {
                if (!MM.Checked && !ShinyCharm.Checked)
                {
                    row.DefaultCellStyle.BackColor = Color.White;
                    row.Cells["dgv_psv"].Style.BackColor = row.Cells["dgv_pid"].Style.BackColor = result.Shiny ? Color.LightCyan : DefaultBackColor;
                }
                if (index == 0) row.DefaultCellStyle.BackColor = DefaultBackColor;
            }

            Frames[index].Formatted = true;

            bool?[] ivsflag = (result as EggResult)?.InheritMaleIV ?? (result as MainRNGEgg)?.InheritMaleIV;
            const int ivstart = 5;
            if (ivsflag != null)
            {
                if (RB_EggShortest.Checked && Frames[index].FrameUsed == EGGACCEPT_STR[lindex, 1])
                    row.DefaultCellStyle.BackColor = Color.LightYellow;

                for (int k = 0; k < 6; k++)
                {
                    if (ivsflag[k] != null)
                    { row.Cells[ivstart + k].Style.ForeColor = (ivsflag[k] == true) ? Color.Blue : Color.DeepPink; continue; }
                    if (result.IVs[k] > 29)
                    { row.Cells[ivstart + k].Style.ForeColor = Color.MediumSeaGreen; row.Cells[ivstart + k].Style.Font = BoldFont; }
                }
                return;
            }
            for (int k = 0; k < 6; k++)
            {
                if (result.IVs[k] < 1)
                {
                    row.Cells[ivstart + k].Style.Font = BoldFont;
                    row.Cells[ivstart + k].Style.ForeColor = Color.OrangeRed;
                }
                else if (result.IVs[k] > 29)
                {
                    row.Cells[ivstart + k].Style.Font = BoldFont;
                    row.Cells[ivstart + k].Style.ForeColor = Color.MediumSeaGreen;
                }
            }
        }
        #endregion

        #region Gen6 Search
        private void Search6()
        {
            switch (Method)
            {
                case 0: if (CreateTimeline.Checked) { Search6_Timeline(); return; } goto default;
                case 2: if (IsHorde) { Search6_Horde(); return; } goto default;
                case 3: Search6_Egg(); return;
                case 4: Search6_ID(); return;
                default: Search6_Normal(); return;
            }
        }

        private void Search6_Normal()
        {
            var rng = new MersenneTwister((uint)Seed.Value);
            int min = (int)Frame_min.Value;
            int max = (int)Frame_max.Value;
            if (AroundTarget.Checked)
            {
                min = (int)TargetFrame.Value - 100; max = (int)TargetFrame.Value + 100;
            }
            // Advance
            for (int i = 0; i < min; i++)
                rng.Next();
            // Prepare
            getsetting(rng);
            // Start
            for (int i = min; i <= max; i++, RNGPool.AddNext(rng))
            {
                RNGResult result = RNGPool.Generate6();
                if (!filter.CheckResult(result))
                    continue;
                Frames.Add(new Frame(result, frame: i, time: i - min));
                if (Frames.Count > 100000)
                    break;
            }
        }

        private void Search6_Timeline()
        {
            var rng = new MersenneTwister((uint)Seed.Value);
            int min = (int)Frame_min.Value;
            int max = (int)TimeSpan.Value * 60 + min;
            // Advance
            for (int i = 0; i < min; i++)
                rng.Next();
            // Prepare
            getsetting(rng);
            var tiny = new TinyStatus(Gen6Tiny);
            RNGPool.tiny = new TinyStatus(Gen6Tiny);
            // Start
            for (int i = min; i <= max; i += 2, RNGPool.AddNext(rng), RNGPool.AddNext(rng), tiny.NextState())
            {
                RNGPool.TinyAdvance(tiny);
                RNGResult result = RNGPool.Generate6();
                if (!filter.CheckResult(result))
                    continue;
                Frames.Add(new Frame(result, frame: i, time: i - min));
                Frames.Last()._tinystate = tiny.State;
                if (Frames.Count > 100000)
                    break;
            }
        }

        private void Search6_Horde()
        {
            var rng = new MersenneTwister((uint)Seed.Value);
            int min = (int)Frame_min.Value;
            int max = (int)Frame_max.Value;
            if (AroundTarget.Checked)
            {
                min = (int)TargetFrame.Value - 100; max = (int)TargetFrame.Value + 100;
            }
            // Advance
            for (int i = 0; i < min; i++)
                rng.Next();
            // Prepare
            getsetting(rng);
            // Start
            for (int i = min; i <= max; i++, RNGPool.AddNext(rng))
            {
                var results = RNGPool.GenerateHorde6();
                foreach (var result in results)
                {
                    if (!filter.CheckResult(result))
                        continue;
                    Frames.Add(new Frame(result, frame: i, time: i - min));
                }
                if (Frames.Count > 500000)
                    break;
            }
        }

        private void Search6_Egg()
        {
            var rng = new MersenneTwister((uint)Seed.Value);
            int min = (int)Frame_min.Value;
            int max = (int)Frame_max.Value;
            if (AroundTarget.Checked)
            {
                min = (int)TargetFrame.Value - 100; max = (int)TargetFrame.Value + 100;
            }
            // Advance
            for (int i = 0; i < min; i++)
                rng.Next();
            // Prepare
            getsetting(rng);

            // The egg already have
            uint[] key = { (uint)Key0.Value, (uint)Key1.Value };
            var eggnow = RNGPool.GenerateAnEgg6(key);
            eggnow.hiddenpower = (byte)Pokemon.getHiddenPowerValue(eggnow.IVs);
            if (RNGPool.IsMainRNGEgg) eggnow.PID = 0xFFFFFFFF;
            eggnow.Status = "Current";
            Frames.Add(new Frame(eggnow, frame: -1));

            // Start
            for (int i = min; i <= max; i++, RNGPool.AddNext(rng))
            {
                var result = RNGPool.GenerateEgg6();
                if (!filter.CheckResult(result))
                    continue;
                Frames.Add(new Frame(result, frame: i, time: i - min));
                if (Frames.Count > 100000)
                    return;
            }
        }

        private void Search6_ID()
        {
            var rng = new TinyMT(new uint[] { (uint)ID_Tiny0.Value, (uint)ID_Tiny1.Value, (uint)ID_Tiny2.Value, (uint)ID_Tiny3.Value });
            int min = Advanced.Checked ? 0 :(int)Frame_min.Value;
            int max = (int)Frame_max.Value;
            IDFrames.Clear();
            DGV_ID.DataSource = null;
            Frame_ID.correction = 0xFF;
            IDFilters idfilter = getIDFilter();
            for (int i = 0; i < min; i++)
                rng.Next();
            for (int i = min; i <= max; i++)
            {
                var result = new ID6(rng);
                if (!idfilter.CheckResult(result))
                    continue;
                IDFrames.Add(new Frame_ID(result, i));
            }
        }
        #endregion

        #region Gen7 Search
        private void Search7()
        {
            if (Method == 4)
            {
                Frame_min.Value = Math.Max(Frame_min.Value, 1012);
                Search7_ID();
                return;
            }
            if (Method == 3 && !MainRNGEgg.Checked)
            {
                if (EggNumber.Checked)
                    Search7_EggList();
                else if (RB_EggShortest.Checked)
                    Search7_EggShortestPath();
                else
                    Search7_Egg();
                return;
            }
            Frame_min.Value = Math.Max(Frame_min.Value, 418);
            // method 0-2 & MainRNGEgg
            if (CreateTimeline.Checked)
                Search7_Timeline();
            else
                Search7_Normal();
        }

        private void Search7_Normal()
        {
            SFMT sfmt = new SFMT((uint)Seed.Value);
            int min = (int)Frame_min.Value;
            int max = (int)Frame_max.Value;
            if (AroundTarget.Checked)
            {
                min = (int)TargetFrame.Value - 100; max = (int)TargetFrame.Value + 100;
            }
            // Blinkflag
            FuncUtil.getblinkflaglist(min, max, sfmt, Modelnum);
            // Advance
            for (int i = 0; i < min; i++)
                sfmt.Next();
            // Prepare
            ModelStatus status = new ModelStatus(Modelnum, sfmt);
            ModelStatus stmp = new ModelStatus(Modelnum, sfmt);
            getsetting(sfmt);
            int frameadvance;
            int realtime = 0;
            int frametime = 0;
            // Start
            for (int i = min; i <= max;)
            {
                do
                {
                    frameadvance = status.NextState();
                    realtime++;
                }
                while (frameadvance == 0); // Keep the starting status of a longlife frame(for npc=0 case)
                do
                {
                    RNGPool.CopyStatus(stmp);
                    var result = RNGPool.Generate7() as Result7;

                    RNGPool.AddNext(sfmt);

                    frameadvance--;
                    i++;
                    if (i <= min || i > max + 1)
                        continue;
                    byte blinkflag = FuncUtil.blinkflaglist[i - min - 1];
                    if (BlinkFOnly.Checked && blinkflag < 4)
                        continue;
                    if (SafeFOnly.Checked && blinkflag >= 2)
                        continue;
                    if (!filter.CheckResult(result))
                        continue;
                    Frames.Add(new Frame(result, frame: i - 1, time: frametime * 2, blink: blinkflag));
                }
                while (frameadvance > 0);

                if (Frames.Count > 100000)
                    return;
                // Backup status of frame
                status.CopyTo(stmp);
                frametime = realtime;
            }
        }

        private void Search7_Timeline()
        {
            SFMT sfmt = new SFMT((uint)Seed.Value);
            int start_frame = (int)Frame_min.Value;
            FuncUtil.getblinkflaglist(start_frame, start_frame, sfmt, Modelnum);
            // Advance
            for (int i = 0; i < start_frame; i++)
                sfmt.Next();
            // Prepare
            ModelStatus status = new ModelStatus(Modelnum, sfmt);
            getsetting(sfmt);
            int totaltime = (int)TimeSpan.Value * 30;
            int frame = (int)Frame_min.Value;
            int frameadvance, Currentframe;
            // Start
            for (int i = 0; i <= totaltime; i++)
            {
                Currentframe = frame;

                RNGPool.CopyStatus(status);

                var result = RNGPool.Generate7() as Result7;

                frameadvance = status.NextState();
                frame += frameadvance;
                for (int j = 0; j < frameadvance; j++)
                    RNGPool.AddNext(sfmt);

                if (!filter.CheckResult(result))
                    continue;

                Frames.Add(new Frame(result, frame: Currentframe, time: i * 2));

                if (Frames.Count > 100000)
                    break;
            }
            if (Frames.FirstOrDefault()?.FrameNum == (int)Frame_min.Value)
                Frames[0].Blink = FuncUtil.blinkflaglist[0];
        }

        private void Search7_Egg()
        {
            var rng = new TinyMT(Status);
            int min = (int)Frame_min.Value;
            int max = (int)Frame_max.Value;
            // Advance
            for (int i = 0; i < min; i++)
                rng.Next();
            // Prepare
            getsetting(rng);
            // Start
            for (int i = min; i <= max; i++, RNGPool.AddNext(rng))
            {
                var result = RNGPool.GenerateEgg7() as EggResult;
                if (!filter.CheckResult(result))
                    continue;
                Frames.Add(new Frame(result, frame: i));
                if (Frames.Count > 100000)
                    return;
            }
        }

        private void Search7_EggList()
        {
            var rng = new TinyMT(Status);
            int min = (int)Egg_min.Value - 1;
            int max = (int)Egg_max.Value - 1;
            int target = (int)TargetFrame.Value;
            bool gotresult = false;
            // Advance
            for (int i = 0; i < min; i++)
                rng.Next();
            TinyMT Seedrng = (TinyMT)rng.DeepCopy();
            // Prepare
            getsetting(rng);
            // Start
            int frame = 0;
            int advance = 0;
            for (int i = 0; i <= max; i++)
            {
                var result = RNGPool.GenerateEgg7() as EggResult;
                advance = result.FramesUsed;
                if (!gotresult && frame <= target && target < frame + advance)
                {
                    Egg_Instruction.Text = getEggListString(i, target - frame);
                    gotresult = true;
                }
                frame += advance;
                for (int j = advance; j > 0; j--)
                    RNGPool.AddNext(rng);
                if (i < min || !filter.CheckResult(result))
                    continue;
                Frames.Add(new Frame(result, frame: frame - advance, eggnum: i + 1));
                if (Frames.Count > 100000)
                    break;
            }
            if (!gotresult)
                Egg_Instruction.Text = getEggListString(-1, -1);
        }

        private void Search7_EggShortestPath()
        {
            var rng = new TinyMT(Status);
            int max = (int)TargetFrame.Value;
            int rejectcount = 0;
            List<EggResult> ResultsList = new List<EggResult>();
            // Prepare
            getsetting(rng);
            // Start
            for (int i = 0; i <= max; i++, RNGPool.AddNext(rng))
                ResultsList.Add(RNGPool.GenerateEgg7() as EggResult);
            var FrameIndexList = Gen7EggPath.Calc(ResultsList.Select(egg => egg.FramesUsed).ToArray());
            max = FrameIndexList.Count;
            for (int i = 0; i < max; i++)
            {
                int index = FrameIndexList[i];
                var result = ResultsList[index];
                result.hiddenpower = (byte)Pokemon.getHiddenPowerValue(result.IVs);
                var Frame = new Frame(result, frame: index, eggnum: i + 1);
                if (i == max - 1 || FrameIndexList[i + 1] - index > 1)
                    Frame.FrameUsed = EGGACCEPT_STR[lindex, 0];
                else
                {
                    Frame.FrameUsed = EGGACCEPT_STR[lindex, 1];
                    rejectcount++;
                }
                Frames.Add(Frame);
            }
            Egg_Instruction.Text = getEggListString(max - rejectcount - 1, rejectcount, true);
        }

        private void Search7_ID()
        {
            SFMT rng = new SFMT((uint)Seed.Value);
            int min = (int)Frame_min.Value;
            int max = (int)Frame_max.Value;
            IDFrames.Clear();
            Frame_ID.correction = (byte)Clk_Correction.Value;
            IDFilters idfilter = getIDFilter();
            for (int i = 0; i < min; i++)
                rng.Next();
            for (int i = min; i <= max; i++)
            {
                var result = new ID7(rng.Nextulong());
                if (!idfilter.CheckResult(result))
                    continue;
                IDFrames.Add(new Frame_ID(result, i));
            }
        }
        #endregion
    }
}