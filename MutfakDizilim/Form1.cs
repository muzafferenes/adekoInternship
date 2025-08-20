using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.IO;

namespace MutfakDizilim
{
    public partial class Form1 : Form
    {
        private PictureBox picCanvas;
        private TextBox txtWall1, txtWall2, txtGPT, txtLog, txtWin, txtWin1, txtWin1D2, txtWinD2;
        private Button BtnOlustur, btnYeniDizilim, btnEskiDizilim, btnGptGonder, btnKuralTemizle;
        private Button btnTestApi;
        private Label lblSonuc, lblWall1, lblWall2, lblKurallar, lblProgress, lblWin, lblWinD2;
        private ListBox lstKurallar;
        private CircularProgressBar circle;
        private List<(int skor, List<(string, int)> dizilim, List<string> log)> tumDizilimler;
        private List<(int skor, List<(string, int)> dizilim, List<string> log)> tumUstDolaplar;
        private int mevcutDizilimIndex = 0;
        private MutfakYerlesimGenerator generator;
        private UstDolapGenerator ustDolapGenerator; // YENİ EKLENEN
        private RadioButton rbtnLDuvar, rbtnDuzDuvar;

        public Form1()
        {
            generator = new MutfakYerlesimGenerator();
            ustDolapGenerator = new UstDolapGenerator(); // YENİ EKLENEN

            this.Size = new Size(1500, 1050);
            this.Text = "Mutfak L Düzeni Çizici - AI Entegreli (1M Dizilim)";
            this.StartPosition = FormStartPosition.CenterScreen;

            InitializeControls();
        }

        private void InitializeControls()
        {
            // Ana panel grupları oluştur
            var headerPanel = CreateHeaderPanel();
            var mainContentPanel = CreateMainContentPanel();
            var sidePanel = CreateSidePanel();

            this.Controls.AddRange(new Control[] { headerPanel, mainContentPanel, sidePanel });
        }

        private Panel CreateHeaderPanel()
        {
            var headerPanel = new Panel
            {
                Location = new Point(0, 0),
                Size = new Size(1460, 90),
                BackColor = Color.FromArgb(248, 249, 250),
                BorderStyle = BorderStyle.FixedSingle
            };

            // Sol grup - Duvar ölçüleri
            var wallGroup = CreateGroupBox("Duvar Ölçüleri", new Point(20, 10), new Size(350, 70));

            lblWall1 = new Label { Text = "Sol Duvar:", Location = new Point(10, 25), Size = new Size(60, 20), Font = new Font("Segoe UI", 9) };
            txtWall1 = new TextBox { Location = new Point(75, 23), Size = new Size(60, 23), Text = "400", Font = new Font("Segoe UI", 9) };
            var lblCm1 = new Label { Text = "cm", Location = new Point(140, 25), Size = new Size(25, 20), Font = new Font("Segoe UI", 8), ForeColor = Color.Gray };

            lblWall2 = new Label { Text = "Alt Duvar:", Location = new Point(175, 25), Size = new Size(60, 20), Font = new Font("Segoe UI", 9) };
            txtWall2 = new TextBox { Location = new Point(240, 23), Size = new Size(60, 23), Text = "400", Font = new Font("Segoe UI", 9) };
            var lblCm2 = new Label { Text = "cm", Location = new Point(305, 25), Size = new Size(25, 20), Font = new Font("Segoe UI", 8), ForeColor = Color.Gray };

            wallGroup.Controls.AddRange(new Control[] { lblWall1, txtWall1, lblCm1, lblWall2, txtWall2, lblCm2 });

            // Orta grup - Pencere (Duvar 1)
            var windowGroup = CreateGroupBox("Pencere Konumu", new Point(375, 10), new Size(220, 70));
            lblWin = new Label { Text = "Başlangıç:", Location = new Point(10, 25), Size = new Size(60, 20), Font = new Font("Segoe UI", 9) };
            txtWin = new TextBox { Location = new Point(75, 23), Size = new Size(50, 23), Text = "200", Font = new Font("Segoe UI", 9) };
            var lblTo = new Label { Text = "-", Location = new Point(130, 25), Size = new Size(10, 20), Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleCenter };
            txtWin1 = new TextBox { Location = new Point(145, 23), Size = new Size(50, 23), Text = "250", Font = new Font("Segoe UI", 9) };
            var lblCm3 = new Label { Text = "cm", Location = new Point(200, 25), Size = new Size(25, 20), Font = new Font("Segoe UI", 8), ForeColor = Color.Gray };
            windowGroup.Controls.AddRange(new Control[] { lblWin, txtWin, lblTo, txtWin1, lblCm3 });

            // Orta grup - Pencere (Duvar 2)
            var windowGroupD2 = CreateGroupBox("Pencere Konumu (Duvar 2)", new Point(windowGroup.Right + 10, 10), new Size(220, 70));
            lblWinD2 = new Label { Text = "Başlangıç:", Location = new Point(10, 25), Size = new Size(60, 20), Font = new Font("Segoe UI", 9) };
            txtWinD2 = new TextBox { Location = new Point(75, 23), Size = new Size(50, 23), Text = "200", Font = new Font("Segoe UI", 9) };
            var lblToD2 = new Label { Text = "-", Location = new Point(130, 25), Size = new Size(10, 20), Font = new Font("Segoe UI", 9), TextAlign = ContentAlignment.MiddleCenter };
            txtWin1D2 = new TextBox { Location = new Point(145, 23), Size = new Size(50, 23), Text = "250", Font = new Font("Segoe UI", 9) };
            var lblCm3D2 = new Label { Text = "cm", Location = new Point(200, 25), Size = new Size(25, 20), Font = new Font("Segoe UI", 8), ForeColor = Color.Gray };
            windowGroupD2.Controls.AddRange(new Control[] { lblWinD2, txtWinD2, lblToD2, txtWin1D2, lblCm3D2 });

            // Sağ grup - Kontroller ve Progress (konum dinamik!)
            var controlGroup = new Panel
            {
                // windowGroupD2'nin hemen sağına koy
                Location = new Point(windowGroupD2.Right + 10, 10),
                Size = new Size(680, 70),
                BackColor = Color.Transparent
            };

            // Progress
            circle = new CircularProgressBar
            {
                Location = new Point(10, 10),
                Size = new Size(50, 50),
                Thickness = 6,
                TrackColor = Color.FromArgb(230, 230, 230),
                ProgressColor = Color.FromArgb(0, 120, 215),
                TextColor = Color.Black,
                ShowPercentage = true,
                Font = new Font("Segoe UI", 8)
            };
            lblProgress = new Label
            {
                Location = new Point(70, 15),
                Size = new Size(80, 40),
                Text = "Hazır",
                Font = new Font("Segoe UI", 9),
                ForeColor = Color.FromArgb(100, 100, 100),
                TextAlign = ContentAlignment.MiddleLeft
            };

            // RadioButtons (headerPanel üzerinde)
            rbtnLDuvar = new RadioButton { Text = "L Duvar", Location = new Point(20, 60), Size = new Size(100, 16), Checked = true };
            rbtnDuzDuvar = new RadioButton { Text = "Düz Duvar", Location = new Point(110, 60), Size = new Size(100, 16), Checked = false };
            headerPanel.Controls.Add(rbtnLDuvar);
            headerPanel.Controls.Add(rbtnDuzDuvar);

            // Butonlar
            BtnOlustur = CreateModernButton("1M Dizilim Oluştur", new Point(160, 10), new Size(140, 35), Color.FromArgb(0, 120, 215), Color.White);
            btnEskiDizilim = CreateModernButton("← Önceki", new Point(305, 10), new Size(80, 35), Color.FromArgb(108, 117, 125), Color.White);
            btnYeniDizilim = CreateModernButton("Sonraki →", new Point(390, 10), new Size(80, 35), Color.FromArgb(108, 117, 125), Color.White);
            btnTestApi = CreateModernButton("API Test", new Point(475, 10), new Size(80, 35), Color.FromArgb(40, 167, 69), Color.White);

            // Click eventleri
            BtnOlustur.Click += async (s, e) => await BtnOlustur_ClickAsync(s, e);
            btnEskiDizilim.Click += btnEskiDizilim_Click;
            btnYeniDizilim.Click += BtnYeniDizilim_Click;
            btnTestApi.Click += async (s, e) => await BtnTestApi_ClickAsync(s, e);

            btnEskiDizilim.Enabled = false;
            btnYeniDizilim.Enabled = false;

            controlGroup.Controls.AddRange(new Control[] { circle, lblProgress, BtnOlustur, btnEskiDizilim, btnYeniDizilim, btnTestApi });

            // EKLEME SIRASI (z-order)
            headerPanel.Controls.Add(wallGroup);
            headerPanel.Controls.Add(windowGroup);
            headerPanel.Controls.Add(windowGroupD2);
            headerPanel.Controls.Add(controlGroup);

            // Her ihtimale karşı öne getir
            controlGroup.BringToFront();

            return headerPanel;
        }


        private Panel CreateMainContentPanel()
        {
            var mainPanel = new Panel
            {
                Location = new Point(0, 90),
                Size = new Size(1080, 850),
                BackColor = Color.White
            };

            // Durum çubuğu
            lblSonuc = new Label
            {
                Location = new Point(20, 10),
                Size = new Size(1040, 25),
                Text = "Duvar ölçülerini girin ve '1M Dizilim Oluştur' düğmesine basın",
                Font = new Font("Segoe UI", 9),
                BackColor = Color.FromArgb(248, 249, 250),
                ForeColor = Color.FromArgb(73, 80, 87),
                Padding = new Padding(10, 5, 10, 5),
                BorderStyle = BorderStyle.FixedSingle
            };

            // Canvas
            picCanvas = new PictureBox
            {
                Location = new Point(20, 45),
                Size = new Size(1040, 785),
                BorderStyle = BorderStyle.None,
                BackColor = Color.White
            };

            // Canvas çerçevesi
            var canvasFrame = new Panel
            {
                Location = new Point(19, 44),
                Size = new Size(1042, 787),
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(233, 236, 239)
            };
            canvasFrame.Controls.Add(picCanvas);

            mainPanel.Controls.AddRange(new Control[] { lblSonuc, canvasFrame });
            return mainPanel;
        }

        private Panel CreateSidePanel()
        {
            var sidePanel = new Panel
            {
                Location = new Point(1080, 90),
                Size = new Size(380, 850),
                BackColor = Color.FromArgb(248, 249, 250),
                BorderStyle = BorderStyle.FixedSingle
            };

            // Log alanı
            var logGroup = CreateGroupBox("İşlem Günlüğü", new Point(15, 15), new Size(350, 220));
            txtLog = new TextBox
            {
                Location = new Point(10, 25),
                Size = new Size(330, 190),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Font = new Font("Consolas", 9),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
            logGroup.Controls.Add(txtLog);

            // AI Kuralları bölümü
            var aiGroup = CreateGroupBox("AI Kuralları", new Point(15, 250), new Size(350, 220));

            btnKuralTemizle = CreateModernButton("Temizle", new Point(270, 2), new Size(70, 25), Color.FromArgb(220, 53, 69), Color.White);
            btnKuralTemizle.Click += BtnKuralTemizle_Click;
            aiGroup.Controls.Add(btnKuralTemizle);

            lstKurallar = new ListBox
            {
                Location = new Point(10, 30),
                Size = new Size(330, 120),
                Font = new Font("Segoe UI", 8),
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle,
                SelectionMode = SelectionMode.MultiExtended
            };
            aiGroup.Controls.Add(lstKurallar);

            // Kural ekleme alanı
            var ruleInputGroup = CreateGroupBox("Yeni Kural Ekle", new Point(15, 485), new Size(350, 200));

            txtGPT = new TextBox
            {
                Multiline = true,
                Location = new Point(10, 25),
                Size = new Size(330, 130),
                Font = new Font("Segoe UI", 9),
                Text = "AI Kural Örnekleri:\n• buzdolabı ile evye arasında en az 90 cm boşluk olsun\n• fırın evyeye yakın olsun ama buzdolabından uzak\n• bulaşık makinesi köşede olmasın\n• çekmeceler fırının yanında olsun",
                ForeColor = Color.Gray,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.White
            };
            txtGPT.Enter += TxtGPT_Enter;
            txtGPT.Leave += TxtGPT_Leave;

            btnGptGonder = CreateModernButton("AI Kural Ekle", new Point(220, 165), new Size(120, 30), Color.FromArgb(111, 66, 193), Color.White);
            btnGptGonder.Click += async (s, e) => await BtnGptGonder_ClickAsync(s, e);

            ruleInputGroup.Controls.AddRange(new Control[] { txtGPT, btnGptGonder });

            sidePanel.Controls.AddRange(new Control[] { logGroup, aiGroup, ruleInputGroup });
            return sidePanel;
        }

        private GroupBox CreateGroupBox(string title, Point location, Size size)
        {
            return new GroupBox
            {
                Text = title,
                Location = location,
                Size = size,
                Font = new Font("Segoe UI", 9, FontStyle.Bold),
                ForeColor = Color.FromArgb(73, 80, 87),
                BackColor = Color.Transparent
            };
        }

        private Button CreateModernButton(string text, Point location, Size size, Color backColor, Color foreColor)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                BackColor = backColor,
                ForeColor = foreColor,
                Font = new Font("Segoe UI", 9),
                FlatStyle = FlatStyle.Flat,
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };

            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(backColor, 0.1f);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(backColor, 0.1f);

            return button;
        }
        private void SetProgress(int percent, string text = null)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            if (this.InvokeRequired)
            {
                this.Invoke((MethodInvoker)(() => {
                    circle.Value = percent;
                    if (text != null) lblProgress.Text = text;
                }));
            }
            else
            {
                circle.Value = percent;
                if (text != null) lblProgress.Text = text;
            }
        }

        private async Task BtnTestApi_ClickAsync(object sender, EventArgs e)
        {
            btnTestApi.Text = "Test...";
            btnTestApi.Enabled = false;
            this.Cursor = Cursors.WaitCursor;

            try
            {
                // Config dosyası kontrolü
                string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");

                if (!File.Exists(configPath))
                {
                    MessageBox.Show($"❌ config.txt dosyası bulunamadı!\n\nDosya yolu: {configPath}\n\nLütfen bu dosyayı oluşturun ve Groq API key'inizi yazın.",
                                  "Config Dosyası Eksik", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string apiKey = File.ReadAllText(configPath).Trim();

                if (string.IsNullOrEmpty(apiKey))
                {
                    MessageBox.Show("❌ config.txt dosyası boş!\n\nGroq API key'inizi config.txt dosyasına yazın.",
                                  "API Key Eksik", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Groq client test
                using (var groqClient = new GroqClient())
                {
                    string status = groqClient.GetApiKeyStatus();
                    string testResult = await groqClient.TestQuota();

                    MessageBox.Show($"Groq API Durumu:\n{status}\n\nTest Sonucu:\n{testResult}",
                                  "Groq API Test", MessageBoxButtons.OK,
                                  testResult.StartsWith("❌") ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Groq API test hatası: {ex.Message}",
                              "Hata", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnTestApi.Text = "API Test";
                btnTestApi.Enabled = true;
                this.Cursor = Cursors.Default;
            }
        }

        private void TxtGPT_Enter(object sender, EventArgs e)
        {
            if (txtGPT.ForeColor == Color.Gray)
            {
                txtGPT.Text = "";
                txtGPT.ForeColor = Color.Black;
            }
        }

        private void TxtGPT_Leave(object sender, EventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtGPT.Text))
            {
                txtGPT.Text = "AI Kural Örnekleri:\n• buzdolabı ile evye arasında en az 90 cm boşluk olsun\n• fırın evyeye yakın olsun ama buzdolabından uzak\n• bulaşık makinesi köşede olmasın\n• çekmeceler fırının yanında olsun";
                txtGPT.ForeColor = Color.Gray;
            }
        }

        private async Task BtnOlustur_ClickAsync(object sender, EventArgs e)
        {
            int duvar1, duvar2 = 0, pencereBaslangic = 0, pencereBitis = 0;
            bool isDuzDuvar = rbtnDuzDuvar != null && rbtnDuzDuvar.Checked;
            int pencereBaslangicD2 = 0, pencereBitisD2 = 0;

            // 1) Duvar uzunluklarını ve pencere değerlerini oku
            if (isDuzDuvar)
            {
                if (!int.TryParse(txtWall1.Text, out duvar1))
                {
                    MessageBox.Show("Lütfen geçerli bir duvar uzunluğu girin.");
                    return;
                }

                if (!string.IsNullOrEmpty(txtWin.Text))
                    int.TryParse(txtWin.Text, out pencereBaslangic);
                if (!string.IsNullOrEmpty(txtWin1.Text))
                    int.TryParse(txtWin1.Text, out pencereBitis);

                // Duvar 2 penceresi (YENİ)
                if (!string.IsNullOrEmpty(txtWinD2.Text))
                    int.TryParse(txtWinD2.Text, out pencereBaslangicD2);
                if (!string.IsNullOrEmpty(txtWin1D2.Text))
                    int.TryParse(txtWin1D2.Text, out pencereBitisD2);

                if (duvar1 < 65)
                {
                    MessageBox.Show("Duvar uzunluğu en az 65 cm olmalıdır.");
                    return;
                }

                if (duvar1 < 300)
                {
                    MessageBox.Show("Bu ölçülerde tüm gerekli modüller sığmayabilir.");
                    return;
                }
            }
            else
            {
                if (!int.TryParse(txtWall1.Text, out duvar1) ||
                    !int.TryParse(txtWall2.Text, out duvar2))
                {
                    MessageBox.Show("Lütfen her iki duvar için de geçerli sayılar girin.");
                    return;
                }

                if (!string.IsNullOrEmpty(txtWin.Text))
                    int.TryParse(txtWin.Text, out pencereBaslangic);
                if (!string.IsNullOrEmpty(txtWin1.Text))
                    int.TryParse(txtWin1.Text, out pencereBitis);

                // Duvar 2 penceresi için de aynı textbox'ları kullanıyorsak
                // (eğer farklı textbox'lar varsa onları da ekleyebiliriz)
                if (!string.IsNullOrEmpty(txtWinD2.Text))
                    int.TryParse(txtWinD2.Text, out pencereBaslangicD2);
                if (!string.IsNullOrEmpty(txtWin1D2.Text))
                    int.TryParse(txtWin1D2.Text, out pencereBitisD2);

                if (duvar1 < 65 || duvar2 < 65)
                {
                    MessageBox.Show("Her iki duvar da en az 65 cm olmalıdır.");
                    return;
                }

                if (duvar1 + duvar2 < 445)
                {
                    MessageBox.Show("Bu ölçülerde tüm gerekli modüller sığmayabilir.");
                    return;
                }
            }

            // 2) Pencere pozisyon kontrolü (Duvar 1 için)
            if (pencereBaslangic > 0 || pencereBitis > 0)
            {
                if (pencereBaslangic >= pencereBitis)
                {
                    MessageBox.Show("Geçersiz Duvar 1 pencere pozisyonu. Başlangıç < Bitiş olmalıdır.");
                    return;
                }

                int duvar1Uzunlugu = duvar1;
                if (pencereBitis > duvar1Uzunlugu)
                {
                    MessageBox.Show($"Duvar 1 pencere pozisyonu duvar uzunluğundan ({duvar1Uzunlugu}cm) büyük olamaz.");
                    return;
                }
            }

            // 3) Pencere pozisyon kontrolü (Duvar 2 için - sadece L şeklinde)
            if (!isDuzDuvar && (pencereBaslangicD2 > 0 || pencereBitisD2 > 0))
            {
                if (pencereBaslangicD2 >= pencereBitisD2)
                {
                    MessageBox.Show("Geçersiz Duvar 2 pencere pozisyonu. Başlangıç < Bitiş olmalıdır.");
                    return;
                }

                if (pencereBitisD2 > duvar2)
                {
                    MessageBox.Show($"Duvar 2 pencere pozisyonu duvar uzunluğundan ({duvar2}cm) büyük olamaz.");
                    return;
                }
            }

            // 4) UI'ı kilitle ve hazırlık yap
            BtnOlustur.Text = "İşleniyor...";
            BtnOlustur.Enabled = false;
            btnYeniDizilim.Enabled = false;
            btnEskiDizilim.Enabled = false;
            btnGptGonder.Enabled = false;

            int kuralSayisi = generator.GetUserRules().Count;
            int ustKuralSayisi = ustDolapGenerator.GetUserRules().Count;

            lblSonuc.Text = $"1,000,000 alt + üst dizilim oluşturuluyor... ({kuralSayisi + ustKuralSayisi} AI kuralı dahil)";
            SetProgress(0, "Alt dolaplar başlıyor...");
            this.Cursor = Cursors.WaitCursor;
            Application.DoEvents();

            try
            {
                // 5) Pencere bilgilerini alt & üst jeneratörlere aktar (YENİ METHOD)
                generator.SetPencereKonumlari(pencereBaslangic, pencereBitis, pencereBaslangicD2, pencereBitisD2);
                ustDolapGenerator.SetPencereKonumlari(pencereBaslangic, pencereBitis, pencereBaslangicD2, pencereBitisD2);

                // 6) Alt dolap dizilimlerini üret
                SetProgress(10, "Alt dolaplar oluşturuluyor...");
                Application.DoEvents();

                if (isDuzDuvar)
                    tumDizilimler = await Task.Run(() => generator.Uret(duvar1, 0));
                else
                    tumDizilimler = await Task.Run(() => generator.Uret(duvar1, duvar2));

                tumDizilimler = tumDizilimler.OrderByDescending(t => t.skor).ToList();

                if (tumDizilimler.Count == 0)
                {
                    MessageBox.Show("Kurallarla uyumlu alt dizilim bulunamadı (hard constraint).");
                    return;
                }

                // 7) En iyi alt dizilimler üzerinden üst dolapları oluştur
                var enIyiAltDizilimler = tumDizilimler.Take(1_000_000).ToList();
                var tumUstSonuclar = new List<(int altIndex, int ustSkor, List<(string, int)> ustDizilim, List<string> ustLog)>();

                await Task.Run(() =>
                {
                    int total = enIyiAltDizilimler.Count;
                    for (int i = 0; i < total; i++)
                    {
                        var altDizilim = enIyiAltDizilimler[i].dizilim;
                        List<(string, int)> ustDizilim;

                        if (isDuzDuvar)
                            ustDizilim = ustDolapGenerator.TekUstDolapDizilimi(duvar1, 0, altDizilim);
                        else
                            ustDizilim = ustDolapGenerator.TekUstDolapDizilimi(duvar1, duvar2, altDizilim);

                        List<string> ustLog;
                        int toplamDuvar = isDuzDuvar ? duvar1 : duvar1 + duvar2;
                        int ustSkor = ustDolapGenerator.Evaluate(ustDizilim, toplamDuvar, out ustLog);

                        tumUstSonuclar.Add((i, ustSkor, ustDizilim, ustLog));

                        if (i % 100000 == 0)
                        {
                            int percent = 50 + (int)(50.0 * i / total);
                            SetProgress(percent, $"Üst dolaplar: {i}/{total} tamamlandı...");
                        }
                    }
                });

                // 8) Alt + üst skorlarını birleştir
                var kombineEdilmisSonuclar = new List<(int toplamSkor, int altSkor, int ustSkor,
                                                       List<(string, int)> altDizilim, List<(string, int)> ustDizilim,
                                                       List<string> altLog, List<string> ustLog)>();

                for (int i = 0; i < tumUstSonuclar.Count; i++)
                {
                    var ustSonuc = tumUstSonuclar[i];
                    var altSonuc = enIyiAltDizilimler[ustSonuc.altIndex];

                    int toplamSkor = altSonuc.skor + ustSonuc.ustSkor;

                    kombineEdilmisSonuclar.Add((
                        toplamSkor,
                        altSonuc.skor,
                        ustSonuc.ustSkor,
                        altSonuc.dizilim,
                        ustSonuc.ustDizilim,
                        altSonuc.log,
                        ustSonuc.ustLog
                    ));
                }

                var siraliSonuclar = kombineEdilmisSonuclar.OrderByDescending(x => x.toplamSkor).ToList();

                // 9) Sonuç listelerini güncelle ve ilk dizilimi çiz
                tumDizilimler = siraliSonuclar.Select(x => (x.toplamSkor, x.altDizilim, x.altLog)).ToList();
                tumUstDolaplar = siraliSonuclar.Select(x => (x.ustSkor, x.ustDizilim, x.ustLog)).ToList();

                SetProgress(100, $"Tamamlandı! {tumDizilimler.Count:N0} kombine dizilim oluşturuldu.");
                mevcutDizilimIndex = 0;

                if (tumDizilimler.Count > 0)
                {
                    if (isDuzDuvar)
                    {
                        CizDizilimDuzDuvar();    // alt
                        CizDizilimUstDuzDuvar(); // üst
                    }
                    else
                    {
                        CizDizilim();            // alt (L)
                        CizDizilimUstPuanli();   // üst (L)
                    }

                    GuncelleButonDurumlari();

                    var topScores = siraliSonuclar.Take(10)
                        .Select(x => $"T:{x.toplamSkor}(A:{x.altSkor}+Ü:{x.ustSkor})")
                        .ToList();

                    MessageBox.Show(
                        $"✅ 1,000,000 alt + 1,000 üst dizilim tamamlandı!\n\n" +
                        $"En yüksek toplam skorlar:\n{string.Join("\n", topScores)}",
                        "Başarılı",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information
                    );
                }
                else
                {
                    MessageBox.Show("Bu ölçülerde uygun dizilim bulunamadı.");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Dizilim oluşturulurken hata: {ex.Message}");
                lblSonuc.Text = "Hata oluştu";
                SetProgress(0, "Hata oluştu");
            }
            finally
            {
                BtnOlustur.Text = "1M Dizilim Oluştur";
                BtnOlustur.Enabled = true;
                btnGptGonder.Enabled = true;
                this.Cursor = Cursors.Default;
            }
        }





        private void CizDizilimUstPuanli()
        {
            if (tumUstDolaplar == null || tumUstDolaplar.Count == 0) return;

            var ustDizilim = tumUstDolaplar[mevcutDizilimIndex].dizilim;
            CizLDuzeniUst(ustDizilim);
        }

        private void GuncelleButonDurumlari()
        {
            btnEskiDizilim.Enabled = mevcutDizilimIndex > 0;
            btnYeniDizilim.Enabled = tumDizilimler != null && mevcutDizilimIndex < tumDizilimler.Count - 1;
        }

        private void BtnYeniDizilim_Click(object sender, EventArgs e)
        {
            if (tumDizilimler == null) return;
            if (mevcutDizilimIndex < tumDizilimler.Count - 1)
            {
                mevcutDizilimIndex++;
                CizMevcutDizilim();
            }
        }

        private void btnEskiDizilim_Click(object sender, EventArgs e)
        {
            if (tumDizilimler == null) return;
            if (mevcutDizilimIndex > 0)
            {
                mevcutDizilimIndex--;
                CizMevcutDizilim();
            }
        }


        private async Task BtnGptGonder_ClickAsync(object sender, EventArgs e)
        {
            if (txtGPT.ForeColor == Color.Gray || string.IsNullOrWhiteSpace(txtGPT.Text))
            {
                MessageBox.Show("Lütfen bir AI kuralı yazın.");
                return;
            }

            string userRule = txtGPT.Text.Trim();

            try
            {
                // AI ile kural işleme
                btnGptGonder.Text = "AI işliyor...";
                btnGptGonder.Enabled = false;
                this.Cursor = Cursors.WaitCursor;

                await generator.AddUserRuleAsync(userRule);

                lstKurallar.Items.Add($"{lstKurallar.Items.Count + 1}. {userRule}");
                txtGPT.Text = "";
                TxtGPT_Leave(null, null);

                MessageBox.Show($"✅ AI kuralı başarıyla eklendi!\n\n'{userRule}'\n\nYeni 1M dizilim oluşturmak için '1M Dizilim Oluştur' butonuna basın.",
                              "AI Kural Eklendi", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"❌ AI kural eklenirken hata: {ex.Message}\n\nLütfen:\n1. API Test butonuna basın\n2. İnternet bağlantınızı kontrol edin\n3. config.txt dosyasındaki API key'i kontrol edin",
                              "AI Hatası", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                btnGptGonder.Text = "AI Kural Ekle";
                btnGptGonder.Enabled = true;
                this.Cursor = Cursors.Default;
            }
        }

        

        private void BtnKuralTemizle_Click(object sender, EventArgs e)
        {
            generator.ClearUserRules();
            lstKurallar.Items.Clear();
            MessageBox.Show("✅ Tüm AI kuralları temizlendi.", "Kurallar Temizlendi", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void GuncelleLog(List<string> loglar)
        {
            txtLog.Clear();
            foreach (var satir in loglar)
            {
                txtLog.AppendText(satir + Environment.NewLine);
            }
        }      
        private void CizDizilim()
{
    if (tumDizilimler == null || tumDizilimler.Count == 0) return;

    var (toplamSkor, dizilim, loglar) = tumDizilimler[mevcutDizilimIndex];
    
    // Üst dolap skoru da varsa göster
    string skorBilgisi = $"Skor: {toplamSkor}";
    if (tumUstDolaplar != null && tumUstDolaplar.Count > mevcutDizilimIndex)
    {
        var ustSkor = tumUstDolaplar[mevcutDizilimIndex].skor;
        var altSkor = toplamSkor - ustSkor;
        skorBilgisi = $"Toplam Skor: {toplamSkor} (Alt:{altSkor} + Üst:{ustSkor})";
    }
    
    lblSonuc.Text = $"Dizilim {mevcutDizilimIndex + 1:N0}/{tumDizilimler.Count:N0} - {skorBilgisi} [AI Enhanced]";
    CizLDuzeni(dizilim);
    GuncelleLog(loglar);
}
        private void CizMevcutDizilim()
        {
            if (tumDizilimler == null || tumDizilimler.Count == 0) return;

            if (rbtnDuzDuvar.Checked)
            {
                // Düz duvar alt + üst
                CizDizilimDuzDuvar();
                CizDizilimUstDuzDuvar();
            }
            else
            {
                // L duvar alt + üst
                CizDizilim();
                CizDizilimUstPuanli();
            }

            GuncelleButonDurumlari();
        }

        private void CizLDuzeni(List<(string, int)> moduller)
        {
            int duvar1 = int.Parse(txtWall1.Text);
            int duvar2 = int.Parse(txtWall2.Text);
            int panelGenislik = picCanvas.Width;
            int panelYukseklik = picCanvas.Height;
            float olcekX = (float)(panelGenislik - 250) / duvar1;
            float olcekY = (float)(panelYukseklik - 250) / duvar2;
            float olcek = Math.Min(olcekX, olcekY);
            int yukseklik = (int)(60 * olcek);
            int baslangicX = 20;
            int baslangicY = 20;
            Bitmap bmp = new Bitmap(picCanvas.Width, picCanvas.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);
                int x = baslangicX;
                int y = baslangicY;
                bool dikey = false;
                bool koseGecildi = false;
                // Alt dolap bilgilerini topla
                List<string> altDolapBilgileri = new List<string>();
                foreach (var modul in moduller)
                {
                    string type = modul.Item1;
                    int width = modul.Item2;
                    Brush renk = GetModulRengi(type);
                    int olcekliGenislik = (int)(width * olcek);
                    // Alt dolap bilgisini listeye ekle (sadece _2 değilse)
                    if (!type.Contains("_2"))
                    {
                        string temizType = type.Replace("_1", "").Replace("_2", "");
                        if (temizType.StartsWith("kose"))
                        {
                            // Köşe modülü için gerçek boyutları göster
                            var koseBilgileri = GetKoseBilgileri(temizType);
                            altDolapBilgileri.Add($"{temizType} ({koseBilgileri.width1}x{koseBilgileri.width2}cm)");
                        }
                        else
                        {
                            // Normal modüller
                            altDolapBilgileri.Add($"{temizType} ({width}cm)");
                        }
                    }
                    if (type.Contains("kose"))
                    {
                        if (!koseGecildi)
                        {
                            CizKoseModulu(g, type, x, y, olcek, yukseklik, renk);
                            var koseBilgileri = GetKoseBilgileri(type);
                            // Köşe geçişini düzgün hesapla
                            x = x + (int)(koseBilgileri.width1 * olcek) - yukseklik;
                            y = y + (int)(koseBilgileri.width2 * olcek);
                            dikey = true;
                            koseGecildi = true;
                        }
                        continue;
                    }
                    Rectangle rect = dikey
                        ? new Rectangle(x, y, yukseklik, olcekliGenislik)
                        : new Rectangle(x, y, olcekliGenislik, yukseklik);
                    if (dikey)
                        y += olcekliGenislik;
                    else
                        x += olcekliGenislik;
                    g.FillRectangle(renk, rect);
                    g.DrawRectangle(Pens.Black, rect);

                    // YENİ ETİKET SİSTEMİ - Kısaltma + boyut, alt çizgiye yakın
                    string kisaEtiket = GetKisaEtiket(type);
                    string etiket = $"{kisaEtiket}\n{width}";

                    using (Font font = new Font("Tahoma", Math.Max(8, (int)(6 * olcek))))
                    {
                        // Alt çizgiye yakın konum hesapla
                        float etiketY = dikey ?
                            rect.Y + rect.Height - 35 : // Dikey modüller için
                            rect.Y + rect.Height - 25;  // Yatay modüller için

                        g.DrawString(etiket, font, Brushes.Black, rect.X + 3, etiketY);
                    }
                }
                
            }
            picCanvas.Image = bmp;
        }

        // Bu helper metodu Form1 sınıfına ekleyin:
        private string GetKisaEtiket(string type)
        {
            string temizType = type.Replace("_1", "").Replace("_2", "").Replace("_3", "");

            return temizType switch
            {
                var t when t.Contains("buzdolabi") => "BUZ",
                var t when t.Contains("evye") => "EVY",
                var t when t.Contains("firin") => "FIR",
                var t when t.Contains("bulasik") => "BUL",
                var t when t.Contains("cekmece") => "ÇEK",
                var t when t.Contains("dolap") => "DOL",
                var t when t.Contains("kose") => "KÖŞ",
                var t when t.Contains("kiler") => "KİL",
                _ => temizType.Length > 3 ? temizType.Substring(0, 3).ToUpper() : temizType.ToUpper()
            };
        }

        // Form1.cs'deki CizLDuzeniUst metodunu bu şekilde güncelleyin:

        // Form1.cs içindeki CizLDuzeniUst metodunu bu şekilde düzeltin:

        // Form1.cs içindeki CizLDuzeniUst metodunu bu şekilde düzeltin:

        private void CizLDuzeniUst(List<(string, int)> ustDolapModulleri)
        {
            // Mevcut çizimi al
            Bitmap mevcutBmp = picCanvas.Image as Bitmap;
            if (mevcutBmp == null) return;

            // Mevcut çizimin kopyasını oluştur
            Bitmap bmp = new Bitmap(mevcutBmp);

            int duvar1 = int.Parse(txtWall1.Text);
            int duvar2 = int.Parse(txtWall2.Text);

            int panelGenislik = picCanvas.Width;
            int panelYukseklik = picCanvas.Height;

            float olcekX = (float)(panelGenislik - 250) / duvar1;
            float olcekY = (float)(panelYukseklik - 250) / duvar2;
            float olcek = Math.Min(olcekX, olcekY);

            int yukseklik = (int)(35 * olcek);
            int baslangicX = 20;
            int baslangicY = 20;

            using (Graphics g = Graphics.FromImage(bmp))
            {
                int x = baslangicX;
                int y = baslangicY;
                bool dikey = false;
                bool koseGecildi = false;

                // ÜST DOLAPLAR İÇİN ÇİZİM
                for (int i = 0; i < ustDolapModulleri.Count; i++)
                {
                    var ustModul = ustDolapModulleri[i];
                    string type = ustModul.Item1;
                    int width = ustModul.Item2;
                    Brush renk = Brushes.LightGray;
                    int olcekliGenislik = (int)(width * olcek);

                    // Köşe modülü _1 parçası
                    if (type.Contains("ust_kose") && type.Contains("_1"))
                    {
                        CizUstKoseModulu(g, type, x, y, olcek, yukseklik, renk);
                        var ustKoseBilgileri = GetUstKoseBilgileri(type);

                        // Koordinatları köşe geçişi için ayarla
                        x = x + (int)(ustKoseBilgileri.width1 * olcek) - yukseklik;
                        y = y + yukseklik;
                        dikey = true;
                        koseGecildi = true;
                        continue;
                    }

                    // Köşe modülü _2 parçası - sadece atla, çizim zaten _1'de yapıldı
                    if (type.Contains("ust_kose") && type.Contains("_2"))
                    {
                        // _2 parçası için ek Y koordinatı ayarlaması
                        var ustKoseBilgileri = GetUstKoseBilgileri(type.Replace("_2", ""));
                        // Y koordinatını _2 parçasının sonuna taşı
                        y = y + (int)(ustKoseBilgileri.width2 * olcek) - yukseklik;
                        continue;
                    }

                    // Boşluk kontrolü
                    if (type == "bosluk")
                    {
                        Rectangle boşlukRect = dikey
                            ? new Rectangle(x, y, yukseklik, olcekliGenislik)
                            : new Rectangle(x, y, olcekliGenislik, yukseklik);

                        // Boşluğu açık gri ile göster
                        g.FillRectangle(Brushes.Red, boşlukRect);
                        

                        

                        if (dikey)
                            y += olcekliGenislik;
                        else
                            x += olcekliGenislik;
                        continue;
                    }

                    // Fırın boşluğu kontrolü (YENİ)
                    if (type == "bosluk2")
                    {
                        Rectangle boşlukRect = dikey
                            ? new Rectangle(x, y, yukseklik, olcekliGenislik)
                            : new Rectangle(x, y, olcekliGenislik, yukseklik);

                        // Boşluğu farklı renkte göster
                        g.FillRectangle(Brushes.Lavender, boşlukRect);
                        g.DrawRectangle(Pens.MediumPurple, boşlukRect);

                        // "Fırın Üstü" yazısı
                        using (Font font = new Font("Arial", Math.Max(6, (int)(7 * olcek))))
                        {
                            g.DrawString("Aspiratör", font, Brushes.Purple,
                                boşlukRect.X + 2, boşlukRect.Y + boşlukRect.Height / 2 - 15);
                        }

                        if (dikey)
                            y += olcekliGenislik;
                        else
                            x += olcekliGenislik;
                        continue;
                    }
                    if (type == "bosluk1")
                    {
                        Rectangle boşlukRect = dikey
                            ? new Rectangle(x, y, yukseklik, olcekliGenislik)
                            : new Rectangle(x, y, olcekliGenislik, yukseklik);

                        // Boşluğu farklı renkte göster
                        g.FillRectangle(Brushes.Brown, boşlukRect);
                        

                        // "Fırın Üstü" yazısı
                        

                        if (dikey)
                            y += olcekliGenislik;
                        else
                            x += olcekliGenislik;
                        continue;
                    }
                    if (type == "pencere")
                    {
                        Rectangle boşlukRect = dikey
                            ? new Rectangle(x, y, yukseklik, olcekliGenislik)
                            : new Rectangle(x, y, olcekliGenislik, yukseklik);

                        // Boşluğu farklı renkte göster
                        g.FillRectangle(Brushes.LightBlue, boşlukRect);
                        g.DrawRectangle(Pens.Black, boşlukRect);

                        string etiketPen = $"PEN\n{width}";
                        using (Font font = new Font("Arial", Math.Max(7, (int)(8 * olcek))))
                        {
                            g.DrawString(etiketPen, font, Brushes.Black, boşlukRect.X + 2, boşlukRect.Y + 2);
                        }

                        if (dikey)
                            y += olcekliGenislik;
                        else
                            x += olcekliGenislik;
                        continue;
                    }
                    if (type == "kalanAlanıDoldurma")
                    {
                        Rectangle boşlukRect = dikey
                            ? new Rectangle(x, y, yukseklik, olcekliGenislik)
                            : new Rectangle(x, y, olcekliGenislik, yukseklik);

                        // Boşluğu açık gri ile göster
                        g.FillRectangle(Brushes.WhiteSmoke, boşlukRect);
                        g.DrawRectangle(Pens.LightGray, boşlukRect);

                        // "Kalan Alan" yazısı
                        using (Font font = new Font("Arial", Math.Max(6, (int)(7 * olcek))))
                        {
                            g.DrawString("KalanAlan", font, Brushes.Gray,
                                boşlukRect.X + 2, boşlukRect.Y + boşlukRect.Height / 2 - 10);
                        }

                        if (dikey)
                            y += olcekliGenislik;
                        else
                            x += olcekliGenislik;
                        continue;
                    }

                    // Normal üst dolap çizimi
                    Rectangle rect = dikey
                        ? new Rectangle(x, y, yukseklik, olcekliGenislik)
                        : new Rectangle(x, y, olcekliGenislik, yukseklik);

                    if (dikey)
                        y += olcekliGenislik;
                    else
                        x += olcekliGenislik;

                    // Üst çizimi
                    g.FillRectangle(renk, rect);
                    g.DrawRectangle(Pens.Black, rect);

                    string etiket = $"ÜST\n{width}";
                    using (Font font = new Font("Arial", Math.Max(7, (int)(8 * olcek))))
                    {
                        g.DrawString(etiket, font, Brushes.Black, rect.X + 2, rect.Y + 2);
                    }
                }
            }

            picCanvas.Image = bmp;
        }

        // CizDizilimUst metodunu da debug için güncelleyin:

        private void CizDizilimUst()
        {
            if (tumDizilimler == null || tumDizilimler.Count == 0) return;

            

            // Her seferinde mevcut alt dizilimi al  
            var mevcutAltDizilim = tumDizilimler[mevcutDizilimIndex].dizilim;

            
            foreach (var mod in mevcutAltDizilim)
            {
                
                if (mod.Item1.Contains("buzdolabi"))
                {
                    Console.WriteLine($"    *** BUZDOLABI BULUNDU! ***");
                }
            }

            // O alt dizilim için üst dolap oluştur
            int duvar1 = int.Parse(txtWall1.Text);
            int duvar2 = int.Parse(txtWall2.Text);

            var ustDizilim = ustDolapGenerator.TekUstDolapDizilimi(duvar1, duvar2, mevcutAltDizilim);

            
            foreach (var mod in ustDizilim)
            {
                
            }

            // Yeni üst dizilimi çiz
            CizLDuzeniUst(ustDizilim);
        }

        // Üst köşe modülü çizimi
        private void CizUstKoseModulu(Graphics g, string type, int x, int y, double olcek, int yukseklik, Brush renk)
        {
            var koseBilgileri = GetUstKoseBilgileri(type);
            int w1 = koseBilgileri.width1;
            int w2 = koseBilgileri.width2;

            int w1Olcekli = (int)(w1 * olcek);
            int w2Olcekli = (int)(w2 * olcek);

            // *** HİZALAMA DÜZELTMESİ ***
            // Yatay parça - tam hizalanmış
            Rectangle rectYatay = new Rectangle(x, y, w1Olcekli, yukseklik);

            // Dikey parça - köşeye tam hizalanmış
            Rectangle rectDikey = new Rectangle(x + w1Olcekli - yukseklik, y, yukseklik, yukseklik + w2Olcekli);

            g.FillRectangle(renk, rectYatay);
            g.FillRectangle(renk, rectDikey);
            g.DrawRectangle(Pens.Black, rectYatay);
            g.DrawRectangle(Pens.Black, rectDikey);

            using (Font font = new Font("Arial", Math.Max(8, (int)(10 * olcek))))
            {
                g.DrawString($"ÜST KÖŞE\n{w1}x{w2}", font, Brushes.Black, x + 2, y + 2);
            }
        }

        // Üst köşe bilgilerini al - UstDolapGenerator'daki köşe boyutları
        private (int width1, int width2) GetUstKoseBilgileri(string type)
        {
            string temizType = type.Replace("_1", "").Replace("_2", "");

            if (temizType == "ust_kose60x60")
                return (60, 60);
            else if (temizType == "ust_kose65x65")
                return (65, 65);
            else
                return (60, 60);
        }

        private void CizKoseModulu(Graphics g, string type, int x, int y, double olcek, int yukseklik, Brush renk)
        {
            var koseBilgileri = GetKoseBilgileri(type);
            int w1 = koseBilgileri.width1;
            int w2 = koseBilgileri.width2;
            int w1Olcekli = (int)(w1 * olcek);
            int w2Olcekli = (int)(w2 * olcek);
            Rectangle rectYatay = new Rectangle(x, y, w1Olcekli, yukseklik);
            Rectangle rectDikey = new Rectangle(x + w1Olcekli - yukseklik, y, yukseklik, yukseklik + w2Olcekli);

            g.FillRectangle(renk, rectYatay);
            g.FillRectangle(renk, rectDikey);
            g.DrawRectangle(Pens.Black, rectYatay);
            g.DrawRectangle(Pens.Black, rectDikey);

            using (Font font = new Font("Tahoma", Math.Max(8, (int)(10 * olcek))))
            {
                // Kısaltma etiketini al
                string kisaEtiket = GetKisaEtiket(type);
                string etiket = $"{kisaEtiket}\n{w1}x{w2}";

                // Alt çizgiye yakın konum hesapla - yatay kısmın alt tarafı
                float etiketY = rectYatay.Y + rectYatay.Height - 25;

                g.DrawString(etiket, font, Brushes.Black, x + 3, etiketY);
            }
        }

        private (int width1, int width2) GetKoseBilgileri(string type)
        {
            string temizType = type.Replace("_1", "").Replace("_2", "").Replace("_3", "");

            if (temizType == "kose90x90")
                return (90, 90);
            else if (temizType == "kose65x120")
                return (65, 120);
            else if (temizType == "kose120x65")
                return (120, 65);
            else
                return (90, 90);
        }

        // Sadece Düz Duvar çizim metodları

        // --- ALT: Tek (Düz) Duvar için üst seviye çağrı ---
        private void CizDizilimDuzDuvar()
        {
            if (tumDizilimler == null || tumDizilimler.Count == 0) return;

            var (toplamSkor, dizilim, loglar) = tumDizilimler[mevcutDizilimIndex];

            string skorBilgisi = $"Skor: {toplamSkor}";
            if (tumUstDolaplar != null && tumUstDolaplar.Count > mevcutDizilimIndex)
            {
                var ustSkor = tumUstDolaplar[mevcutDizilimIndex].skor;
                var altSkor = toplamSkor - ustSkor;
                skorBilgisi = $"Toplam Skor: {toplamSkor} (Alt:{altSkor} + Üst:{ustSkor})";
            }

            lblSonuc.Text = $"Dizilim {mevcutDizilimIndex + 1:N0}/{tumDizilimler.Count:N0} - {skorBilgisi} [AI Enhanced - Tek Duvar]";
            CizDuzDuvarDuzeni(dizilim);
            GuncelleLog(loglar);
        }

        // --- ALT: Tek (Düz) Duvar çizimi (sadece DUVAR1, köşe yok) ---
        private void CizDuzDuvarDuzeni(List<(string, int)> moduller)
        {
            int duvar1 = int.Parse(txtWall1.Text);

            // Köşe modüllerini tamamen at; yalnızca yatay diz
            var cizilecek = moduller
                .Where(m => !m.Item1.Contains("kose")) // alt köşe yok
                .ToList();

            // Modül toplamı ile gerçek duvar uzunluğunun hangisi uzunsa ona göre ölçekle
            int modToplam = cizilecek.Sum(m => m.Item2);
            int referansGenislik = Math.Max(duvar1, modToplam);

            int panelGenislik = picCanvas.Width;
            int panelYukseklik = picCanvas.Height;

            float olcek = Math.Min((float)(panelGenislik - 100) / referansGenislik,
                                   (float)(panelYukseklik - 200) / 100f);

            int yukseklik = (int)(60 * olcek);
            int baslangicX = 50;
            int baslangicY = panelYukseklik / 2; // ortalı çizelim

            Bitmap bmp = new Bitmap(picCanvas.Width, picCanvas.Height);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.White);

                // Duvar çizgisi (referansa göre)
                int duvarY = baslangicY + yukseklik + 10;
                int duvarCizimUzunlugu = (int)(referansGenislik * olcek);
                g.DrawLine(Pens.Gray, baslangicX, duvarY, baslangicX + duvarCizimUzunlugu, duvarY);

                // Duvar etiketi
                using (Font font = new Font("Arial", 9))
                    g.DrawString($"Duvar: {duvar1} cm", font, Brushes.Black, baslangicX, duvarY + 15);

                // Pencere (yalnızca duvar1 üzerinde)
                if (int.TryParse(txtWin.Text, out int pBas) &&
                    int.TryParse(txtWin1.Text, out int pBit) &&
                    pBas > 0 && pBit > pBas)
                {
                    // pencereyi duvar1 ile sınırla
                    pBas = Math.Max(0, Math.Min(pBas, duvar1));
                    pBit = Math.Max(0, Math.Min(pBit, duvar1));

                    int pencereX = baslangicX + (int)(pBas * olcek);
                    int pencereGen = (int)((pBit - pBas) * olcek);

                    Rectangle pencereRect = new Rectangle(pencereX, baslangicY - 40, pencereGen, 30);
                    g.FillRectangle(Brushes.LightBlue, pencereRect);
                    g.DrawRectangle(Pens.Blue, pencereRect);
                    using (Font font = new Font("Arial", 8))
                        g.DrawString("PENCERE", font, Brushes.Blue, pencereRect.X + 5, pencereRect.Y + 8);
                }

                // Modülleri sırayla tek hat üzerinde çiz
                int x = baslangicX;
                foreach (var (type, width) in cizilecek)
                {
                    // Her ihtimale karşı "kose" içereni atla
                    if (type.Contains("kose")) continue;

                    int w = (int)(width * olcek);
                    var renk = GetModulRengi(type);
                    var rect = new Rectangle(x, baslangicY, w, yukseklik);
                    x += w;

                    g.FillRectangle(renk, rect);
                    g.DrawRectangle(Pens.Black, rect);

                    string kisa = GetKisaEtiket(type.Replace("_1", "").Replace("_2", ""));
                    using (Font font = new Font("Tahoma", Math.Max(8, (int)(8 * olcek))))
                        g.DrawString($"{kisa}\n{width}", font, Brushes.Black, rect.X + 3, rect.Y + rect.Height - 25);
                }
            }

            picCanvas.Image = bmp;
        }

        // --- ÜST: Tek (Düz) Duvar için üst seviye çağrı ---
        private void CizDizilimUstDuzDuvar()
        {
            if (tumUstDolaplar == null || tumUstDolaplar.Count == 0) return;
            var ustDizilim = tumUstDolaplar[mevcutDizilimIndex].dizilim;
            CizDuzDuvarUstDizilimi(ustDizilim);
        }

        // --- ÜST: Tek (Düz) Duvar çizimi (sadece DUVAR1, üst-köşe yok) ---
        private void CizDuzDuvarUstDizilimi(List<(string, int)> ustDolapModulleri)
        {
            // Alt çizim yoksa dön
            if (picCanvas.Image == null) return;

            Bitmap bmp = new Bitmap(picCanvas.Image as Bitmap);
            int duvar1 = int.Parse(txtWall1.Text);

            var cizilecek = ustDolapModulleri
                .Where(m => !m.Item1.Contains("ust_kose")) // üst köşe yok
                .ToList();

            int modToplam = cizilecek.Sum(m => m.Item2);
            int referansGenislik = Math.Max(duvar1, modToplam);

            int panelGenislik = picCanvas.Width;
            int panelYukseklik = picCanvas.Height;

            float olcek = Math.Min((float)(panelGenislik - 100) / referansGenislik,
                                   (float)(panelYukseklik - 200) / 100f);

            int yukseklik = (int)(35 * olcek);     // üstler daha kısa
            int baslangicX = 50;
            int baslangicY = panelYukseklik / 2 - 50; // altın üstünde

            using (Graphics g = Graphics.FromImage(bmp))
            {
                int x = baslangicX;

                foreach (var (type, width) in cizilecek)
                {
                    // üstte köşe yok
                    if (type.Contains("ust_kose")) continue;

                    int w = (int)(width * olcek);
                    var rect = new Rectangle(x, baslangicY, w, yukseklik);
                    x += w;

                    // Türlere göre görünüm
                    if (type == "bosluk")
                    {
                        g.FillRectangle(Brushes.Red, rect);
                        using (Font font = new Font("Arial", Math.Max(6, (int)(7 * olcek))))
                            g.DrawString("BOŞLUK", font, Brushes.Black, rect.X + 2, rect.Y + rect.Height / 2 - 8);
                    }
                    else if (type == "bosluk1")
                    {
                        g.FillRectangle(Brushes.Brown, rect);
                    }
                    else if (type == "bosluk2")
                    {
                        g.FillRectangle(Brushes.Lavender, rect);
                        g.DrawRectangle(Pens.MediumPurple, rect);
                        using (Font font = new Font("Arial", Math.Max(6, (int)(7 * olcek))))
                            g.DrawString("ASPİRATÖR", font, Brushes.Purple, rect.X + 2, rect.Y + rect.Height / 2 - 8);
                    }
                    else if (type == "pencere")
                    {
                        g.FillRectangle(Brushes.LightBlue, rect);
                        g.DrawRectangle(Pens.Black, rect);
                        using (Font font = new Font("Arial", Math.Max(7, (int)(8 * olcek))))
                            g.DrawString($"PEN\n{width}", font, Brushes.Black, rect.X + 2, rect.Y + 2);
                    }
                    else if (type == "kalanAlanıDoldurma")
                    {
                        g.FillRectangle(Brushes.WhiteSmoke, rect);
                        g.DrawRectangle(Pens.LightGray, rect);
                        using (Font font = new Font("Arial", Math.Max(6, (int)(7 * olcek))))
                            g.DrawString("KALAN", font, Brushes.Gray, rect.X + 2, rect.Y + rect.Height / 2 - 10);
                    }
                    else
                    {
                        g.FillRectangle(Brushes.LightGray, rect);
                        g.DrawRectangle(Pens.Black, rect);

                        using (Font font = new Font("Arial", Math.Max(7, (int)(8 * olcek))))
                            g.DrawString($"ÜST\n{width}", font, Brushes.Black, rect.X + 2, rect.Y + 2);
                    }
                }
            }

            picCanvas.Image = bmp;
        }


        private Brush GetModulRengi(string type) =>
            type.Contains("buzdolabi") ? Brushes.Red :
            type.Contains("evye") ? Brushes.Green :
            type.Contains("firin") ? Brushes.Purple :
            type.Contains("bulasik") ? Brushes.Blue :
            type.Contains("cekmece") ? Brushes.LightBlue :
            type.Contains("dolap") ? Brushes.LightGray :
            type.Contains("kose") ? Brushes.Orange :
            type.Contains("kiler") ? Brushes.Brown : Brushes.White;

        

        

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            // Generator'ları dispose et
            generator?.Dispose();
            ustDolapGenerator?.Dispose(); // YENİ EKLENEN
            base.OnFormClosed(e);
        }
    }
}