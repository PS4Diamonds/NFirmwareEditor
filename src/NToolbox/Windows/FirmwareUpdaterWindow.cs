﻿using System;
using System.ComponentModel;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using NCore;
using NCore.UI;
using NCore.USB;
using NCore.USB.Models;
using NFirmware;

namespace NToolbox.Windows
{
	internal partial class FirmwareUpdaterWindow : WindowBase
	{
		private readonly FirmwareLoader m_loader = new FirmwareLoader(new FirmwareEncoder());
		private readonly BackgroundWorker m_worker = new BackgroundWorker { WorkerReportsProgress = true };

		private SimpleDataflash m_dataflash;
		private HidDeviceInfo m_deviceInfo = HidDeviceInfo.UnknownDevice;
		private string m_connectedDeviceProductId;
		private string m_hardwareVersion;
		private string m_firmwareVersion;

		public FirmwareUpdaterWindow()
		{
			InitializeComponent();
			InitializeControls();

			m_worker.DoWork += BackgroundWorker_DoWork;
			m_worker.ProgressChanged += BackgroundWorker_ProgressChanged;

			HidConnector.Instance.DeviceConnected += DeviceConnected;
			Load += (s, e) =>
			{
				new Thread(() => DeviceConnected(HidConnector.Instance.LastConnectionState)) { IsBackground = true }.Start();
			};
			Closing += (s, e) =>
			{
				if (!CancelButton.Enabled)
				{
					e.Cancel = true;
					return;
				}
				HidConnector.Instance.DeviceConnected -= DeviceConnected;
			};
		}

		private void DeviceConnected(bool isConnected)
		{
			if (isConnected)
			{
				System.Diagnostics.Trace.WriteLine("Connected " + DateTime.Now);

				try
				{
					m_dataflash = HidConnector.Instance.ReadDataflash();
				}
				catch
				{
					return;
				}

				m_connectedDeviceProductId = m_dataflash.ProductId;
				m_deviceInfo = HidDeviceInfo.Get(m_connectedDeviceProductId);
				m_hardwareVersion = (m_dataflash.HardwareVersion / 100f).ToString("0.00", CultureInfo.InvariantCulture);
				m_firmwareVersion = (m_dataflash.FirmwareVersion / 100f).ToString("0.00", CultureInfo.InvariantCulture);

				UpdateUI(() =>
				{
					DeviceNameTextBox.Text = m_deviceInfo.Name;
					HardwareVersionTextBox.Text = m_hardwareVersion;
					FirmwareVersionTextBox.Text = m_firmwareVersion;
					BootModeTextBox.Text = m_dataflash.LoadFromLdrom ? "LDROM" : "APROM";
					UpdateStatusLabel.Text = @"Device is ready.";
					SetUpdaterButtonsState(true);
				});
			}
			else
			{
				System.Diagnostics.Trace.WriteLine("Disconnected " + DateTime.Now);
				m_connectedDeviceProductId = null;
				m_dataflash = null;

				UpdateUI(() =>
				{
					DeviceNameTextBox.Clear();
					HardwareVersionTextBox.Clear();
					FirmwareVersionTextBox.Clear();
					BootModeTextBox.Clear();
					UpdateStatusLabel.Text = @"Waiting for device...";
					SetUpdaterButtonsState(false);
				});
			}
		}

		private void InitializeControls()
		{
			DeviceNameTextBox.BackColor = Color.White;
			HardwareVersionTextBox.BackColor = Color.White;
			FirmwareVersionTextBox.BackColor = Color.White;
			BootModeTextBox.BackColor = Color.White;

			DeviceNameTextBox.ReadOnly = true;
			HardwareVersionTextBox.ReadOnly = true;
			FirmwareVersionTextBox.ReadOnly = true;
			BootModeTextBox.ReadOnly = true;

			UpdateFromFileButton.Click += UpdateFromFileButton_Click;

			ResetDataflashButton.Click += ResetDataflashButton_Click;
			ReadDataflashButton.Click += ReadDataflashButton_Click;
			WriteDataflashButton.Click += WriteDataflashButton_Click;

			ChangeHWButton.Click += ChangeHWButton_Click;
			ChangeBootModeButton.Click += ChangeBootModeButton_Click;
		}

		private void UpdateFirmware(Func<byte[]> firmwareFunc)
		{
			var skipValidation = ModifierKeys.HasFlag(Keys.Control) && ModifierKeys.HasFlag(Keys.Alt);
			try
			{
				var firmware = firmwareFunc();
				if (!skipValidation && firmware.FindByteArray(Encoding.UTF8.GetBytes(m_connectedDeviceProductId)) == -1)
				{
					InfoBox.Show("Opened firmware file is not suitable for the connected device.");
					return;
				}
				m_worker.RunWorkerAsync(new AsyncProcessWrapper(worker => UpdateFirmwareAsyncWorker(worker, firmware)));
			}
			catch (Exception ex)
			{
				Trace.Warn(ex);
				InfoBox.Show("An error occured during firmware update.\n" + ex.Message);
			}
		}

		private void UpdateFirmwareAsyncWorker(BackgroundWorker worker, byte[] firmware)
		{
			var isSuccess = false;
			try
			{
				UpdateUI(() => UpdateStatusLabel.Text = @"Reading dataflash...");
				Trace.Info("Reading dataflash...");
				var dataflash = HidConnector.Instance.ReadDataflash(worker);
				Trace.Info("Reading dataflash... Done.");

				if (dataflash.LoadFromLdrom == false && dataflash.FirmwareVersion > 0)
				{
					Trace.Info("Switching boot mode...");
					dataflash.LoadFromLdrom = true;

					UpdateUI(() => UpdateStatusLabel.Text = @"Writing dataflash...");
					Trace.Info("Writing dataflash...");
					HidConnector.Instance.WriteDataflash(dataflash, worker);
					Trace.Info("Writing dataflash... Done. Waiting 500 msec.");
					Thread.Sleep(500);

					UpdateUI(() => UpdateStatusLabel.Text = @"Restarting device...");
					Trace.Info("Restarting device...");
					HidConnector.Instance.RestartDevice();
					Trace.Info("Restarting device... Done. Waiting 3000 msec.");
					Thread.Sleep(3000);

					Trace.Info("Waiting for device after reset...");
					UpdateUI(() => UpdateStatusLabel.Text = @"Waiting for device after reset...");

					var result = SpinWait.SpinUntil(() =>
					{
						Thread.Sleep(100);
						return HidConnector.Instance.IsDeviceConnected;
					}, TimeSpan.FromSeconds(5));

					if (result) Trace.Info("Waiting for device after reset... Done.");
					if (!result)
					{
						Trace.Info("Waiting for device after reset... Failed.");
						InfoBox.Show("Device is not connected. Update process interrupted.");
						return;
					}
				}

				UpdateUI(() => UpdateStatusLabel.Text = @"Uploading firmware...");

				Trace.Info("Uploading firmware...");
				HidConnector.Instance.WriteFirmware(firmware, worker);
				Trace.Info("Uploading firmware... Done.");

				UpdateUI(() =>
				{
					UpdateStatusLabel.Text = string.Empty;
					worker.ReportProgress(0);
				});
				isSuccess = true;

				Thread.Sleep(500);
			}
			catch (Exception ex)
			{
				Trace.Warn(ex);
				InfoBox.Show("An exception occured during firmware update.\n" + ex.Message);
			}
			finally
			{
				if (isSuccess)
				{
					InfoBox.Show("Firmware successfully updated.");
				}
			}
		}

		private void ReadDataflashAsyncWorker(BackgroundWorker worker, string fileName)
		{
			try
			{
				UpdateUI(() => UpdateStatusLabel.Text = @"Reading dataflash...");
				var dataflash = HidConnector.Instance.ReadDataflash(worker);
				File.WriteAllBytes(fileName, dataflash.Data);
				UpdateUI(() => UpdateStatusLabel.Text = @"Dataflash was successfully read and saved to the file.");
			}
			catch (Exception ex)
			{
				Trace.Warn(ex);
				InfoBox.Show("An exception occured during dataflash reading.\n" + ex.Message);
			}
		}

		private void WriteDataflashAsyncWorker(BackgroundWorker worker, SimpleDataflash simpleDataflash)
		{
			try
			{
				UpdateUI(() => UpdateStatusLabel.Text = @"Writing dataflash...");
				HidConnector.Instance.WriteDataflash(simpleDataflash, worker);
				UpdateUI(() =>
				{
					UpdateStatusLabel.Text = @"Dataflash was successfully written.";
					worker.ReportProgress(0);
				});
			}
			catch (Exception ex)
			{
				Trace.Warn(ex);
				InfoBox.Show("An exception occured during dataflash reading.\n" + ex.Message);
			}
		}

		private void SetUpdaterButtonsState(bool enabled)
		{
			UpdateFromFileButton.Enabled = enabled;

			ResetDataflashButton.Enabled = enabled;
			ReadDataflashButton.Enabled = enabled;
			WriteDataflashButton.Enabled = enabled;

			ChangeHWButton.Enabled = enabled;
			ChangeBootModeButton.Enabled = enabled;
		}

		private void SetAllButtonsState(bool enabled)
		{
			SetUpdaterButtonsState(enabled);
			CancelButton.Enabled = enabled;
		}

		private string GetDataflashFileName()
		{
			return string.Format("{0} HW v{1} FW v{2}", m_deviceInfo.Name, m_hardwareVersion, m_firmwareVersion);
		}

		private void UpdateFromFileButton_Click(object sender, EventArgs e)
		{
			string fileName;
			using (var op = new OpenFileDialog { Title = @"Select encrypted or decrypted firmware file ...", Filter = FileFilters.FirmwareFilter })
			{
				if (op.ShowDialog() != DialogResult.OK) return;
				fileName = op.FileName;
			}
			UpdateFirmware(() => m_loader.LoadFile(fileName));
		}

		private void ResetDataflashButton_Click(object sender, EventArgs e)
		{
			try
			{
				HidConnector.Instance.ResetDataflash();
				UpdateStatusLabel.Text = @"Dataflash has been reseted.";
			}
			catch (Exception ex)
			{
				Trace.Warn(ex);
				InfoBox.Show("An error occured during dataflash reset.\n" + ex.Message);
			}
		}

		private void ReadDataflashButton_Click(object sender, EventArgs e)
		{
			string fileName;
			using (var sf = new SaveFileDialog { Title = @"Select dataflash file location ...", Filter = FileFilters.DataflashFilter, FileName = GetDataflashFileName() })
			{
				if (sf.ShowDialog() != DialogResult.OK) return;
				fileName = sf.FileName;
			}

			try
			{
				File.WriteAllBytes(fileName, new byte[1]);
				m_worker.RunWorkerAsync(new AsyncProcessWrapper(worker => ReadDataflashAsyncWorker(worker, fileName)));
			}
			catch (Exception ex)
			{
				Trace.Warn(ex);
				InfoBox.Show("An error occured during dataflash reading.\n" + ex.Message);
			}
		}

		private void WriteDataflashButton_Click(object sender, EventArgs e)
		{
			string fileName;
			using (var op = new OpenFileDialog { Title = @"Select dataflash file ...", Filter = FileFilters.DataflashFilter })
			{
				if (op.ShowDialog() != DialogResult.OK) return;
				fileName = op.FileName;
			}

			try
			{
				var data = File.ReadAllBytes(fileName);
				if (data.Length != 2044)
				{
					InfoBox.Show("Seems that the dataflash file has the wrong format.");
					return;
				}

				var dataflash = new SimpleDataflash { Data = data };
				m_worker.RunWorkerAsync(new AsyncProcessWrapper(worker => WriteDataflashAsyncWorker(worker, dataflash)));
			}
			catch (Exception ex)
			{
				Trace.Warn(ex);
				InfoBox.Show("An error occured during dataflash write.\n" + ex.Message);
			}
		}

		private void ChangeHWButton_Click(object sender, EventArgs e)
		{
			if (m_dataflash == null) return;

			using (var hwVersionDialog = new HardwareVersionWindow(m_dataflash.HardwareVersion))
			{
				if (hwVersionDialog.ShowDialog() != DialogResult.OK) return;
				m_dataflash.HardwareVersion = hwVersionDialog.GetNewHWVersion();
			}

			try
			{
				m_worker.RunWorkerAsync(new AsyncProcessWrapper(worker =>
				{
					WriteDataflashAsyncWorker(worker, m_dataflash);
					HidConnector.Instance.RestartDevice();
				}));
			}
			catch (Exception ex)
			{
				Trace.Warn(ex);
				InfoBox.Show("An error occured during dataflash write.\n" + ex.Message);
			}
		}

		private void ChangeBootModeButton_Click(object sender, EventArgs e)
		{
			if (m_dataflash == null) return;

			m_dataflash.LoadFromLdrom = !m_dataflash.LoadFromLdrom;
			try
			{
				m_worker.RunWorkerAsync(new AsyncProcessWrapper(worker =>
				{
					WriteDataflashAsyncWorker(worker, m_dataflash);
					HidConnector.Instance.RestartDevice();
				}));
			}
			catch (Exception ex)
			{
				Trace.Warn(ex);
				InfoBox.Show("An error occured during switching boot mode.\n" + ex.Message);
			}
		}

		private void BackgroundWorker_ProgressChanged(object sender, ProgressChangedEventArgs e)
		{
			UpdateProgressBar.Value = e.ProgressPercentage;
		}

		private void BackgroundWorker_DoWork(object sender, DoWorkEventArgs e)
		{
			var worker = (BackgroundWorker)sender;
			var wrapper = (AsyncProcessWrapper)e.Argument;

			try
			{
				UpdateUI(() => SetAllButtonsState(false));
				HidConnector.Instance.StopUSBConnectionMonitoring();
				wrapper.Processor(worker);
			}
			finally
			{
				UpdateUI(() => SetAllButtonsState(true));
				HidConnector.Instance.StartUSBConnectionMonitoring();
			}
		}
	}
}
