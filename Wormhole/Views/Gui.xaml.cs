﻿using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Wormhole.ViewModels;

namespace Wormhole.Views
{
    public partial class Gui : UserControl
    {
        public Gui()
        {
            InitializeComponent();
        }

        public Gui(Plugin plugin) : this()
        {
            Plugin = plugin;
            DataContext = plugin.Config;
        }

        private Plugin Plugin { get; }
        private GateViewModel _selectedGate = new();

        private void SaveButton_OnClick(object sender, RoutedEventArgs e)
        {
            Plugin.Save();
        }

        private void Add_OnClick(object sender, RoutedEventArgs e)
        {
            if (!double.TryParse(Xinput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var xCord) ||
                !double.TryParse(Yinput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var yCord) ||
                !double.TryParse(Zinput.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var zCord))
            {
                MessageBox.Show("Invalid Coordinates");
                return;
            }

            _selectedGate.Name = Nameinput.Text;
            _selectedGate.Description = Descriptioninput.Text;
            _selectedGate.HexColor = GateColorPicker.SelectedColor?.ToHexString() ?? string.Empty;
            _selectedGate.X = xCord;
            _selectedGate.Y = yCord;
            _selectedGate.Z = zCord;

            if (Plugin.Config.WormholeGates.Any(b => b.Name == _selectedGate.Name)) return;

            Plugin.Config.WormholeGates.Add(_selectedGate);
            Nameinput.Text = string.Empty;
            Descriptioninput.Text = string.Empty;
            GateColorPicker.IsOpen = false;
            GateColorPicker.SelectedColor = null;
            GateColorPicker.IsEnabled = false;
            Xinput.Text = string.Empty;
            Yinput.Text = string.Empty;
            Zinput.Text = string.Empty;
            SendToHinput.Text = string.Empty;
            _selectedGate = new();
            DestinationsButton.IsEnabled = false;
        }

        private void Del_OnClick(object sender, RoutedEventArgs e)
        {
            if (ListServers.SelectedItem is GateViewModel gate)
                Plugin.Config.WormholeGates.Remove(gate);
        }

        private void Edit_OnClick(object sender, RoutedEventArgs e)
        {
            if (ListServers.SelectedItem is not GateViewModel gate) return;

            Nameinput.Text = gate.Name;
            Descriptioninput.Text = gate.Description;
            try
            {
                GateColorPicker.SelectedColor = (Color)ColorConverter.ConvertFromString(gate.HexColor)!;
            }
            catch (FormatException)
            {
                GateColorPicker.SelectedColor = null;
            }
            GateColorPicker.IsEnabled = true;
            Xinput.Text = gate.X.ToString(CultureInfo.InvariantCulture);
            Yinput.Text = gate.Y.ToString(CultureInfo.InvariantCulture);
            Zinput.Text = gate.Z.ToString(CultureInfo.InvariantCulture);
            SendToHinput.Text = string.Join(";", gate.Destinations.Select(static b => b.Id));
            Plugin.Config.WormholeGates.Remove(gate);
            _selectedGate = gate;
            DestinationsButton.IsEnabled = true;
        }

        private void DestinationsButton_OnClick(object sender, RoutedEventArgs e)
        {
            if (_selectedGate is { } gate)
                new DestinationsEditor(gate).ShowDialog();
        }
    }
}