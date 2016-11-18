﻿using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using NCore;
using NCore.UI;
using NCore.USB;
using NToolbox.Models;

namespace NToolbox.Windows
{
	public partial class ArcticFoxConfigurationWindow : WindowBase
	{
		private const int MinimumSupportedBuildNumber = 161117;

		private readonly BackgroundWorker m_worker = new BackgroundWorker { WorkerReportsProgress = true };
		private readonly HidConnector m_connector = new HidConnector();

		private bool m_isDeviceWasConnectedOnce;
		private bool m_isDeviceConnected;
		private ArcticFoxConfiguration m_configuration;

		public ArcticFoxConfigurationWindow()
		{
			InitializeComponent();
			Initialize();
		}

		private void Initialize()
		{
			m_worker.DoWork += Worker_DoWork;
			m_worker.ProgressChanged += (s, e) => ProgressLabel.Text = e.ProgressPercentage + @"%";
			m_worker.RunWorkerCompleted += (s, e) => ProgressLabel.Text = @"Operation completed";

			MainContainer.SelectedPage = WelcomePage;
			ProfilesTabControl.TabPages.Clear();

			FirmwareVersionTextBox.ReadOnly = true;
			FirmwareVersionTextBox.BackColor = Color.White;

			BuildTextBox.ReadOnly = true;
			BuildTextBox.BackColor = Color.White;

			HardwareVersionTextBox.ReadOnly = true;
			HardwareVersionTextBox.BackColor = Color.White;

			BrightnessTrackBar.ValueChanged += (s, e) => BrightnessPercentLabel.Text = (int)(BrightnessTrackBar.Value * 100m / 255) + @"%";

			var lineContentItems = new object[]
			{
				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Non dominant (Pwr / Temp)", ArcticFoxConfiguration.LineContent.NonDominant),

				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Volts", ArcticFoxConfiguration.LineContent.Volt),
				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Output Volts", ArcticFoxConfiguration.LineContent.Vout),
				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Output Amps", ArcticFoxConfiguration.LineContent.Amps),

				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Resistance", ArcticFoxConfiguration.LineContent.Resistance),
				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Live Resistance", ArcticFoxConfiguration.LineContent.RealResistance),

				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Puffs", ArcticFoxConfiguration.LineContent.Puffs),
				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Puffs Time", ArcticFoxConfiguration.LineContent.Time),
				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Battery(s) Volts", ArcticFoxConfiguration.LineContent.BatteryVolts),
				
				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Date/Time", ArcticFoxConfiguration.LineContent.DateTime),
				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Board Temperature", ArcticFoxConfiguration.LineContent.BoardTemperature),

				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Battery", ArcticFoxConfiguration.LineContent.Battery),
				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Battery + %", ArcticFoxConfiguration.LineContent.BatteryWithPercents),
				new NamedItemContainer<ArcticFoxConfiguration.LineContent>("Battery + V", ArcticFoxConfiguration.LineContent.BatteryWithVolts)
			};

			var linesComboBoxes = new[]
			{
				VWLine1ComboBox, VWLine2ComboBox, VWLine3ComboBox, VWLine4ComboBox,
				TCLine1ComboBox, TCLine2ComboBox, TCLine3ComboBox, TCLine4ComboBox,
			};

			foreach (var lineComboBox in linesComboBoxes)
			{
				lineComboBox.Items.Clear();
				lineComboBox.Items.AddRange(lineContentItems);
			}

			ClockTypeComboBox.Items.Clear();
			ClockTypeComboBox.Items.AddRange(new object[]
			{
				new NamedItemContainer<ArcticFoxConfiguration.ClockType>("Off", ArcticFoxConfiguration.ClockType.Off),
				new NamedItemContainer<ArcticFoxConfiguration.ClockType>("Analog", ArcticFoxConfiguration.ClockType.Analog),
				new NamedItemContainer<ArcticFoxConfiguration.ClockType>("Digital", ArcticFoxConfiguration.ClockType.Digital)
			});

			ScreensaverTimeComboBox.Items.Clear();
			ScreensaverTimeComboBox.Items.AddRange(new object[]
			{
				new NamedItemContainer<ArcticFoxConfiguration.ScreenProtectionTime>("Off", ArcticFoxConfiguration.ScreenProtectionTime.Off),
				new NamedItemContainer<ArcticFoxConfiguration.ScreenProtectionTime>("1 Min", ArcticFoxConfiguration.ScreenProtectionTime.Min1),
				new NamedItemContainer<ArcticFoxConfiguration.ScreenProtectionTime>("2 Min", ArcticFoxConfiguration.ScreenProtectionTime.Min2),
				new NamedItemContainer<ArcticFoxConfiguration.ScreenProtectionTime>("5 Min", ArcticFoxConfiguration.ScreenProtectionTime.Min5),
				new NamedItemContainer<ArcticFoxConfiguration.ScreenProtectionTime>("10 Min", ArcticFoxConfiguration.ScreenProtectionTime.Min10),
				new NamedItemContainer<ArcticFoxConfiguration.ScreenProtectionTime>("15 Min", ArcticFoxConfiguration.ScreenProtectionTime.Min15),
				new NamedItemContainer<ArcticFoxConfiguration.ScreenProtectionTime>("20 Min", ArcticFoxConfiguration.ScreenProtectionTime.Min20),
				new NamedItemContainer<ArcticFoxConfiguration.ScreenProtectionTime>("30 Min", ArcticFoxConfiguration.ScreenProtectionTime.Min30)
			});

			var clickItems = new object[]
			{
				new NamedItemContainer<ArcticFoxConfiguration.ClickAction>("None", ArcticFoxConfiguration.ClickAction.None),

				new NamedItemContainer<ArcticFoxConfiguration.ClickAction>("Edit", ArcticFoxConfiguration.ClickAction.Edit),
				new NamedItemContainer<ArcticFoxConfiguration.ClickAction>("Main Menu", ArcticFoxConfiguration.ClickAction.MainMenu),
				new NamedItemContainer<ArcticFoxConfiguration.ClickAction>("Preheat Menu", ArcticFoxConfiguration.ClickAction.Preheat),

				new NamedItemContainer<ArcticFoxConfiguration.ClickAction>("Select Profile", ArcticFoxConfiguration.ClickAction.ProfileSelector),
				new NamedItemContainer<ArcticFoxConfiguration.ClickAction>("Edit Profile", ArcticFoxConfiguration.ClickAction.ProfileEdit),

				new NamedItemContainer<ArcticFoxConfiguration.ClickAction>("TDom", ArcticFoxConfiguration.ClickAction.TemperatureDominant),
				new NamedItemContainer<ArcticFoxConfiguration.ClickAction>("Show Clock", ArcticFoxConfiguration.ClickAction.MainScreenClock),

				new NamedItemContainer<ArcticFoxConfiguration.ClickAction>("LSL On / Off", ArcticFoxConfiguration.ClickAction.Lsl),
				new NamedItemContainer<ArcticFoxConfiguration.ClickAction>("On / Off", ArcticFoxConfiguration.ClickAction.OnOff)
			};

			var clickComboBoxes = new[] { Clicks2ComboBox, Clicks3ComboBox, Clicks4ComboBox };
			foreach (var clickComboBox in clickComboBoxes)
			{
				clickComboBox.Items.Clear();
				clickComboBox.Items.AddRange(clickItems);
			}

			m_connector.DeviceConnected += DeviceConnected;
			Load += (s, e) => m_connector.StartUSBConnectionMonitoring();
			Closing += (s, e) => m_connector.StopUSBConnectionMonitoring();

			DownloadButton.Click += DownloadButton_Click;
			UploadButton.Click += UploadButton_Click;
			ResetButton.Click += ResetButton_Click;
		}

		private void Worker_DoWork(object sender, DoWorkEventArgs e)
		{
			var worker = (BackgroundWorker)sender;
			var wrapper = (AsyncProcessWrapper)e.Argument;

			try
			{
				UpdateUI(() => SetControlButtonsState(false));
				wrapper.Processor(worker);
			}
			finally
			{
				UpdateUI(() => SetControlButtonsState(true));
			}
		}

		private void SetControlButtonsState(bool enabled)
		{
			DownloadButton.Enabled = UploadButton.Enabled = ResetButton.Enabled = enabled;
		}

		private void DownloadButton_Click(object sender, EventArgs e)
		{
			if (!ValidateConnectionStatus()) return;

			m_worker.RunWorkerAsync(new AsyncProcessWrapper(worker =>
			{
				try
				{
					var configuration = ReadConfiguration();
					if (configuration == null)
					{
						InfoBox.Show("Something strange happened! Please restart application.");
						return;
					}
					m_configuration = configuration;
					UpdateUI(InitializeWorkspace);
				}
				catch (Exception ex)
				{
					//s_logger.Warn(ex);
					InfoBox.Show(GetErrorMessage("downloading settings"));
				}
			}));
		}

		private void UploadButton_Click(object sender, EventArgs e)
		{
			if (!ValidateConnectionStatus()) return;

			m_worker.RunWorkerAsync(new AsyncProcessWrapper(worker =>
			{
				try
				{
					UpdateUI(SaveWorkspace);
					WriteConfiguration();
				}
				catch (Exception ex)
				{
					//s_logger.Warn(ex);
					InfoBox.Show(GetErrorMessage("uploading settings"));
				}
			}));
		}

		private void ResetButton_Click(object sender, EventArgs e)
		{
			if (!ValidateConnectionStatus()) return;

			m_worker.RunWorkerAsync(new AsyncProcessWrapper(worker =>
			{
				try
				{
					m_connector.ResetDataflash();
					DownloadButton_Click(null, null);
				}
				catch (Exception ex)
				{
					//s_logger.Warn(ex);
					InfoBox.Show(GetErrorMessage("resetting settings"));
				}
			}));
		}

		private string GetErrorMessage(string operationName)
		{
			return "An error occurred during " +
				   operationName +
				   "...\n\n" +
				   "To continue, please activate or reconnect your device.";
		}

		private void InitializeWorkspace()
		{
			//MainContainer.
			var deviceInfo = m_configuration.Info;
			{
				DeviceNameLabel.Text = HidDeviceInfo.Get(deviceInfo.ProductId).Name;
				FirmwareVersionTextBox.Text = (deviceInfo.FirmwareVersion / 100f).ToString("0.00", CultureInfo.InvariantCulture);
				BuildTextBox.Text = deviceInfo.FirmwareBuild.ToString();
				HardwareVersionTextBox.Text = (deviceInfo.HardwareVersion / 100f).ToString("0.00", CultureInfo.InvariantCulture);
			}

			var general = m_configuration.General;
			{
				for (var i = 0; i < general.Profiles.Length; i++)
				{
					var tabName = "P" + (i + 1);
					ProfileTabContent tabContent;

					if (ProfilesTabControl.TabPages.Count <= i)
					{
						SelectedProfleComboBox.Items.Add(new NamedItemContainer<byte>(tabName, (byte)i));

						var tabPage = new TabPage(tabName);
						tabContent = new ProfileTabContent(deviceInfo.MaxPower / 10) { Dock = DockStyle.Fill };
						tabPage.Controls.Add(tabContent);
						ProfilesTabControl.TabPages.Add(tabPage);
					}
					else
					{
						tabContent = (ProfileTabContent)ProfilesTabControl.TabPages[i].Controls[0];
					}

					var profile = general.Profiles[i];
					tabContent.Initialize(profile);
					tabContent.UpdateTFRNames(m_configuration.Advanced.TFRTables);
				}

				SelectedProfleComboBox.SelectItem(general.SelectedProfile);
				SmartCheckBox.Checked = general.IsSmartEnabled;
			}

			var ui = m_configuration.Interface;
			{
				BrightnessTrackBar.Value = ui.Brightness;
				IdleTimeUpDow.Value = ui.DimTimeout;
				StealthModeCheckBox.Checked = ui.IsStealthMode;
				FlippedModeCheckBox.Checked = ui.IsFlipped;
				BatteryPercentsCheckBox.Checked = ui.IsBatteryPercents;
				UseClassicMenuCheckBox.Checked = ui.IsClassicMenu;
				ShowLogoCheckBox.Checked = ui.IsLogoEnabled;
				ClockTypeComboBox.SelectItem(ui.ClockType);
				ScreensaverTimeComboBox.SelectItem(ui.ScreensaveDuration);

				InitializeLineContentEditor(ui.VWLines.Line1, VWLine1ComboBox, VWLine1FireCheckBox);
				InitializeLineContentEditor(ui.VWLines.Line2, VWLine2ComboBox, VWLine2FireCheckBox);
				InitializeLineContentEditor(ui.VWLines.Line3, VWLine3ComboBox, VWLine3FireCheckBox);
				InitializeLineContentEditor(ui.VWLines.Line4, VWLine4ComboBox, VWLine4FireCheckBox);

				InitializeLineContentEditor(ui.TCLines.Line1, TCLine1ComboBox, TCLine1FireCheckBox);
				InitializeLineContentEditor(ui.TCLines.Line2, TCLine2ComboBox, TCLine2FireCheckBox);
				InitializeLineContentEditor(ui.TCLines.Line3, TCLine3ComboBox, TCLine3FireCheckBox);
				InitializeLineContentEditor(ui.TCLines.Line4, TCLine4ComboBox, TCLine4FireCheckBox);

				Clicks2ComboBox.SelectItem(ui.Clicks[0]);
				Clicks3ComboBox.SelectItem(ui.Clicks[1]);
				Clicks4ComboBox.SelectItem(ui.Clicks[2]);

				WakeUpByPlusMinusCheckBox.Checked = ui.WakeUpByPlusMinus;
				Step1WCheckBox.Checked = ui.IsPowerStep1W;
			}

			var stats = m_configuration.Counters;
			{
				PuffsUpDown.Value = Math.Max(0, Math.Min(stats.PuffsCount, 99999));
				PuffsTimeUpDown.Value = Math.Max(0, Math.Min(stats.PuffsTime / 10m, 99999));
			}
		}

		private void SaveWorkspace()
		{
			var general = m_configuration.General;
			{
				// Profiles Tab
				for (var i = 0; i < general.Profiles.Length; i++)
				{
					var tabContent = (ProfileTabContent)ProfilesTabControl.TabPages[i].Controls[0];
					tabContent.Save(general.Profiles[i]);
				}

				general.SelectedProfile = SelectedProfleComboBox.GetSelectedItem<byte>();
				general.IsSmartEnabled = SmartCheckBox.Checked;
			}

			var ui = m_configuration.Interface;
			{
				// General -> Screen Tab
				ui.Brightness = (byte)BrightnessTrackBar.Value;
				ui.DimTimeout = (byte)IdleTimeUpDow.Value;
				ui.IsStealthMode = StealthModeCheckBox.Checked;
				ui.IsFlipped = FlippedModeCheckBox.Checked;
				ui.IsBatteryPercents = BatteryPercentsCheckBox.Checked;
				ui.IsClassicMenu = UseClassicMenuCheckBox.Checked;
				ui.IsLogoEnabled = ShowLogoCheckBox.Checked;
				ui.ClockType = ClockTypeComboBox.GetSelectedItem<ArcticFoxConfiguration.ClockType>();
				ui.ScreensaveDuration = ScreensaverTimeComboBox.GetSelectedItem<ArcticFoxConfiguration.ScreenProtectionTime>();

				// General -> Layout Tab
				ui.VWLines.Line1 = SaveLineContent(VWLine1ComboBox, VWLine1FireCheckBox);
				ui.VWLines.Line2 = SaveLineContent(VWLine2ComboBox, VWLine2FireCheckBox);
				ui.VWLines.Line3 = SaveLineContent(VWLine3ComboBox, VWLine3FireCheckBox);
				ui.VWLines.Line4 = SaveLineContent(VWLine4ComboBox, VWLine4FireCheckBox);

				ui.TCLines.Line1 = SaveLineContent(TCLine1ComboBox, TCLine1FireCheckBox);
				ui.TCLines.Line2 = SaveLineContent(TCLine2ComboBox, TCLine2FireCheckBox);
				ui.TCLines.Line3 = SaveLineContent(TCLine3ComboBox, TCLine3FireCheckBox);
				ui.TCLines.Line4 = SaveLineContent(TCLine4ComboBox, TCLine4FireCheckBox);

				// General -> Controls Tab
				ui.Clicks[0] = Clicks2ComboBox.GetSelectedItem<ArcticFoxConfiguration.ClickAction>();
				ui.Clicks[1] = Clicks3ComboBox.GetSelectedItem<ArcticFoxConfiguration.ClickAction>();
				ui.Clicks[2] = Clicks4ComboBox.GetSelectedItem<ArcticFoxConfiguration.ClickAction>();
				ui.WakeUpByPlusMinus = WakeUpByPlusMinusCheckBox.Checked;
				ui.IsPowerStep1W = Step1WCheckBox.Checked;
			}

			var stats = m_configuration.Counters;
			{
				var now = DateTime.Now;

				// General -> Stats Tab
				stats.PuffsCount = (ushort)PuffsUpDown.Value;
				stats.PuffsTime = (ushort)(PuffsTimeUpDown.Value * 10);

				// Time sync
				stats.Year = (ushort)now.Year;
				stats.Month = (byte)now.Month;
				stats.Day = (byte)now.Day;
				stats.Hour = (byte)now.Hour;
				stats.Minute = (byte)now.Minute;
				stats.Second = (byte)now.Second;
			}
		}

		private void InitializeLineContentEditor(ArcticFoxConfiguration.LineContent content, ComboBox comboBox, CheckBox checkBox)
		{
			var contentCopy = content;
			checkBox.Checked = contentCopy.HasFlag(ArcticFoxConfiguration.LineContent.FireTimeMask);
			contentCopy &= ~ArcticFoxConfiguration.LineContent.FireTimeMask;
			comboBox.SelectItem(contentCopy);
		}

		private ArcticFoxConfiguration.LineContent SaveLineContent(ComboBox comboBox, CheckBox checkBox)
		{
			var result = comboBox.GetSelectedItem<ArcticFoxConfiguration.LineContent>();
			if (checkBox.Checked)
			{
				result |= ArcticFoxConfiguration.LineContent.FireTimeMask;
			}
			return result;
		}

		private bool ValidateConnectionStatus()
		{
			while (!m_isDeviceConnected)
			{
				var result = MessageBox.Show
				(
					"No compatible USB devices are connected." +
					"\n\n" +
					"To continue, please connect one." +
					"\n\n" +
					"If one already IS connected, try unplugging and plugging it back in. The cable may be loose.",
					"Information",
					MessageBoxButtons.OKCancel
				);
				if (result == DialogResult.Cancel)
				{
					return false;
				}
			}
			return true;
		}

		private ArcticFoxConfiguration ReadConfiguration(bool useWorker = true)
		{
			byte[] data = null;
			try
			{
				data = m_connector.ReadConfiguration(useWorker ? m_worker : null);
			}
			catch (TimeoutException)
			{
			}
			return data != null ? BinaryStructure.Read<ArcticFoxConfiguration>(data) : null;
		}

		private void WriteConfiguration()
		{
			var data = BinaryStructure.Write(m_configuration);
			try
			{
				m_connector.WriteConfiguration(data, m_worker);
			}
			catch (TimeoutException)
			{
				MessageBox.Show("Error", "Error", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void DeviceConnected(bool isConnected)
		{
			m_isDeviceConnected = isConnected;
			UpdateUI(() => StatusLabel.Text = @"Device is " + (m_isDeviceConnected ? "connected" : "disconnected"));

			if (m_isDeviceWasConnectedOnce) return;

			if (!m_isDeviceConnected)
			{
				UpdateUI(() =>
				{
					UpdateUI(() => WelcomeLabel.Text = string.Format("Connect device with\n\nArctic Fox\n[{0}]\n\nfirmware or newer\nto continue...", MinimumSupportedBuildNumber));
					MainContainer.SelectedPage = WelcomePage;
				});
				return;
			}

			UpdateUI(() => WelcomeLabel.Text = @"Downloading settings...");
			try
			{
				m_configuration = ReadConfiguration(false);
				if (m_configuration == null || m_configuration.Info.FirmwareBuild < MinimumSupportedBuildNumber)
				{
					DeviceConnected(false);
					return;
				}

				UpdateUI(() =>
				{
					InitializeWorkspace();
					MainContainer.SelectedPage = WorkspacePage;
					m_isDeviceWasConnectedOnce = true;
				}, false);
			}
			catch (Exception ex)
			{
				//s_logger.Warn(ex);
				UpdateUI(() => WelcomeLabel.Text = @"Unable to download device settings. Reconnect your device.");
			}
		}
	}
}
