// test push

using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using NAudio.Wave;
using Whisper.net;

namespace SpeechToTextWhisperGUI
{
    public partial class Form1 : Form
    {
        // --- alanlar ---
        private WaveInEvent? waveIn;
        private WaveFileWriter? waveWriter;
        private bool isRecording = false;

        // geçici dosyalar
        private string rawWavPath = "";    // cihazın ham formatında kayıt
        private string finalWavPath = "";  // 16kHz/mono (Whisper sever)
        private string modelPath = "ggml-small.bin"; // projede duran model dosyası

        public Form1()
        {
            InitializeComponent();
            PrepareUI();
            LoadDevicesToCombo();
        }

        // başlangıçta UI'ı temizle
        private void PrepareUI()
        {
            txtOutput.Clear();
            lblStatus.Text = "Hazır";
            btnRecord.Text = "Kaydı Başlat";
            


            // debug görmemiz faydalı
            AppendDebug("Form yüklendi.\r\n");
        }

        // mikrofon listesini comboBox'a doldur
        private void LoadDevicesToCombo()
        {
            cmbDevices.Items.Clear();
            for (int i = 0; i < WaveInEvent.DeviceCount; i++)
            {
                var caps = WaveInEvent.GetCapabilities(i);
                cmbDevices.Items.Add($"{i} - {caps.ProductName}");
            }

            if (cmbDevices.Items.Count > 0)
                cmbDevices.SelectedIndex = 0;
        }

        // Kayıt Başlat / Bitir butonu
        private async void btnRecord_Click(object sender, EventArgs e)
        {
            if (!isRecording)
            {
                // kayıt başlat
                await StartRecordingAsync();
            }
            else
            {
                // kayıt bitir
                await StopRecordingAndTranscribeAsync();
            }
        }

        // ------------------------ 1. KAYDI BAŞLAT ------------------------
        private async Task StartRecordingAsync()
        {
            try
            {
                if (cmbDevices.SelectedIndex < 0)
                {
                    MessageBox.Show("Lütfen bir mikrofon seç.");
                    return;
                }

                // yeni dosya yollarını hazırla
                rawWavPath = Path.Combine(Path.GetTempPath(), "record_raw.wav");
                finalWavPath = Path.Combine(Path.GetTempPath(), "record_tr_16k.wav");

                // varsa eskileri temizle
                SafeDelete(rawWavPath);
                SafeDelete(finalWavPath);

                int deviceIndex = cmbDevices.SelectedIndex;

                // WaveInEvent ile ses yakala
                waveIn = new WaveInEvent
                {
                    DeviceNumber = deviceIndex,
                    WaveFormat = new WaveFormat(16000, 1) // 16kHz mono direkt almaya çalış
                };

                waveWriter = new WaveFileWriter(rawWavPath, waveIn.WaveFormat);

                waveIn.DataAvailable += (s, a) =>
                {
                    waveWriter?.Write(a.Buffer, 0, a.BytesRecorded);
                };

                waveIn.RecordingStopped += (s, a) =>
                {
                    waveWriter?.Dispose();
                    waveWriter = null;
                    waveIn?.Dispose();
                    waveIn = null;
                };

                waveIn.StartRecording();

                isRecording = true;
                btnRecord.Text = "Kaydı Bitir";
                lblStatus.Text = "🎙 Kayıt alınıyor...";
                

                AppendDebug("Kayıt başladı.\r\n");
            }
            catch (Exception ex)
            {
                ShowTranscribeError(ex);
            }

            await Task.CompletedTask;
        }

        // ------------------------ 2. KAYDI DURDUR + WHISPER ------------------------
        private async Task StopRecordingAndTranscribeAsync()
        {
            try
            {
                // kaydı durdur
                if (waveIn != null)
                {
                    waveIn.StopRecording();
                }

                isRecording = false;
                btnRecord.Text = "Kaydı Başlat";
                lblStatus.Text = "Kaydı durdu.";

                AppendDebug("Kayıt durdu.\r\n");

                // bazen disk flush için ufak gecikme iyi oluyor
                await Task.Delay(200);

                // elimizdeki WAV zaten 16kHz/mono aldıysak dönüştürmeye gerek yok
                // ama yine de finalWavPath'e kopyalayalım (Whisper bundan okuyacak)
                File.Copy(rawWavPath, finalWavPath, overwrite: true);

                AppendDebug($"16kHz/mono hazırlandı: {finalWavPath}\r\n");

                // SH 3. Whisper ile çöz – textbox'a yaz
                await TranscribeAndShowAsync(finalWavPath);

                // temizlik
                SafeDelete(rawWavPath);
                SafeDelete(finalWavPath);

                AppendDebug("[debug] Bitti!\r\n");
            }
            catch (Exception ex)
            {
                ShowTranscribeError(ex);
            }
            finally
            {
                
            }
        }

        // ------------------------ 3. WHISPER TRANSCRIBE ------------------------
        private async Task TranscribeAndShowAsync(string wavPath)
        {
            AppendDebug("Ses metne çevriliyor...\r\n");

            // model var mı kontrol et
            if (!File.Exists(modelPath))
            {
                MessageBox.Show(
                    $"Model dosyası bulunamadı:\r\n{modelPath}\r\n\nLütfen ggml-small.bin gibi bir modeli proje klasörüne koy.",
                    "Model Yok",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error
                );
                return;
            }

            // whisper factory / processor
            using var factory = WhisperFactory.FromPath(modelPath);
            using var processor = factory
                .CreateBuilder()
                .WithLanguage("tr")       // Türkçe dilinde çöz
                                          //.WithTranslate(false)    // 1.7.0 sürümünde çeviri seçeneği yok -> kapalı
                .Build();

            // wav'i açıp oku
            using var fileStream = File.OpenRead(wavPath);

            // Whisper.net.ProcessAsync segment segment döndürür
            await foreach (var segment in processor.ProcessAsync(fileStream))
            {
                // zaman bilgisini de yazdırıyoruz
                string line =
                    $"[{segment.Start}->{segment.End}] {segment.Text}\r\n";

                txtOutput.AppendText(line);
            }

            lblStatus.Text = "Bitti!";
        }

        // ------------------------ Yardımcılar ------------------------

        private void AppendDebug(string msg)
        {
            // debug satırlarını da kullanıcıya göstermek istiyoruz
            txtOutput.AppendText("[debug] " + msg);
        }

        private void ShowTranscribeError(Exception ex)
        {
            // hata penceresi
            MessageBox.Show(
                ex.ToString(),
                "Transcribe Hatası",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error
            );

            // debug olarak da textbox'a bas
            txtOutput.AppendText("[hata] " + ex.Message + "\r\n");
        }

        // Geçici dosyaları güvenli şekilde sil (kilitliyse hata atmasın)
        private void SafeDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch
            {
                // dosya kullanımda olabilir -> sessiz geç
            }
        }
    }
}






