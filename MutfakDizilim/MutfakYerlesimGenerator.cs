using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Text.RegularExpressions;
using System.IO;
using System.Threading.Tasks;

namespace MutfakDizilim
{
    internal class MutfakYerlesimGenerator
    {
        private readonly Random rnd = new Random();
        private readonly GroqClient groqClient = new GroqClient(); // Groq API client
        private List<string> userRules = new List<string>();

        // Derlenmiş kuralları cache'lemek için
        private Dictionary<string, Func<List<(string, int)>, int, int>> compiledRules =
            new Dictionary<string, Func<List<(string, int)>, int, int>>();

        private Dictionary<string, List<int>> MODULES = new Dictionary<string, List<int>>
        {
            { "buzdolabi", new List<int> {80, 85} },
            { "evye",      new List<int> {60, 70, 80} },
            { "firin",     new List<int> {60, 70} },
            { "bulasik",   new List<int> {60, 70} },
            { "cekmece",   new List<int> {40, 45, 50, 60, 70, 80, 90, 100} },
            { "dolap",     new List<int> {40, 45, 50, 60, 70, 80, 90, 100 } },
            { "kiler",     new List<int> {50, 60} }
        };

        private List<(string type, int width1, int width2)> CORNER_MODULES = new List<(string, int, int)>
        {
            ("kose90x90", 90, 90),
            ("kose65x120", 65, 120),
            ("kose120x65", 120, 65)
        };
        private int pencereBaslangic = -1;
        private int pencereBitis = -1;
        private bool pencereVarMi = false;

        private int pencereBaslangicD2 = -1;
        private int pencereBitisD2 = -1;
        private bool pencereVarMiD2 = false;

        public void SetPencereKonumlari(int baslangic, int bitis, int baslangicD2, int bitisD2)
        {
            // Duvar 1 penceresi
            pencereBaslangic = baslangic;
            pencereBitis = bitis;
            pencereVarMi = (baslangic >= 0 && bitis > baslangic);

            // Duvar 2 penceresi
            pencereBaslangicD2 = baslangicD2;
            pencereBitisD2 = bitisD2;
            pencereVarMiD2 = (baslangicD2 >= 0 && bitisD2 > baslangicD2);
        }

        // Pencere yasaklı alanını hesapla
        private List<(int basla, int bitir, string sebep)> GetPencereYasakliAlanlar(int duvar1, int duvar2, string modulTipi = null)
            {
                var yasakliAlanlar = new List<(int basla, int bitir, string sebep)>();

                if (!pencereVarMi)
                    return yasakliAlanlar;

                // Modül tipine göre pencere kuralları
                switch (modulTipi)
                {
                    case "buzdolabi":
                    case "kiler":
                    case "firin":
                        // Bu modüller pencere altında OLAMAZ
                        yasakliAlanlar.Add((pencereBaslangic, pencereBitis, "pencere"));
                        break;

                    case "evye":
                    case "bulasik":
                    case "cekmece":
                    case "dolap":
                        // Bu modüller pencere altında OLABİLİR - yasaklı alan ekleme
                        break;

                    default:
                        // Varsayılan: diğer modüller pencere altında olabilir
                        break;
                }

                return yasakliAlanlar;
            }

            // Pencere yasaklı alanlarını duvarlara böl - DÜZELTME
        private void GetDuvarYasakliAlanlari(int duvar1, int duvar2,
            out List<(int, int, string)> duvar1Yasakli,
            out List<(int, int, string)> duvar2Yasakli)
            {
                var pencereYasakliAlanlar = GetPencereYasakliAlanlar(duvar1, duvar2);

                // DUVAR 1 (Yatay) - Pencere genellikle burada
                duvar1Yasakli = pencereYasakliAlanlar
                    .Where(x => x.basla < duvar1) // Sadece duvar1 sınırları içinde
                    .Select(x => (Math.Max(0, x.basla), Math.Min(duvar1, x.bitir), x.sebep))
                    .Where(x => x.Item1 < x.Item2) // Geçerli aralık
                    .ToList();

                // DUVAR 2 (Dikey) - Pencere burada nadiren olur
                duvar2Yasakli = pencereYasakliAlanlar
                    .Where(x => x.basla >= duvar1 && x.basla < duvar1 + duvar2) // Sadece duvar2 aralığında
                    .Select(x => (x.basla - duvar1, x.bitir - duvar1, x.sebep)) // Koordinatları duvar2'ye çevir
                    .Where(x => x.Item1 >= 0 && x.Item1 < duvar2) // Duvar2 sınırları içinde
                    .ToList();
            }

        // Online kural ekleme metodu (Groq ile)
        public async Task AddUserRuleAsync(string rule)
        {
            if (!string.IsNullOrWhiteSpace(rule))
            {
                try
                {
                    // Kuralı derle ve cache'le
                    var compiledRule = await CompileUserRuleAsync(rule);
                    userRules.Add(rule);
                    compiledRules[rule] = compiledRule;
                }
                catch (Exception ex)
                {
                    throw new Exception($"Kural eklenirken hata: {ex.Message}");
                }
            }
        }

        public void ClearUserRules()
        {
            userRules.Clear();
            compiledRules.Clear();
        }

        public List<string> GetUserRules()
        {
            return new List<string>(userRules);
        }

        // Her zaman 1,000,000 dizilim oluştur
        // Yeni kontrol methodu - Kiler buzdolabının yanında mı kontrolü
        private List<(string, int)> GenerateLDuzeni(int duvar1, int duvar2)
        {
            // 1) Pencere yasaklı alanlarını iki duvara ayır
            List<(int, int, string)> d1Yasak, d2Yasak;
            GetDuvarYasakliAlanlari(duvar1, duvar2, out d1Yasak, out d2Yasak);

            // 2) Zorunlu modüller (evye‑fırın‑bulaşık‑buzdolabı) - hepsi zorunlu 1 tane
            List<string> reqTypes = new List<string> { "evye", "firin", "bulasik", "buzdolabi" };
            List<(string, int)> required = new List<(string, int)>();
            foreach (string t in reqTypes)
                required.Add((t, MODULES[t][rnd.Next(MODULES[t].Count)]));

            // 3) Corner modülü seç
            var corner = CORNER_MODULES[rnd.Next(CORNER_MODULES.Count)];
            int yatayMax = duvar1 - corner.width1;
            int dikeyMax = duvar2;

            List<(string, int)> yatay = new List<(string, int)>();
            List<(string, int)> dikey = new List<(string, int)>();
            List<(string, int)> tempReq = new List<(string, int)>(required);

            // 4) Zorunlu modülleri basit şekilde yerleştir (sona ekleme)
            while (tempReq.Count > 0)
            {
                int pick = rnd.Next(tempReq.Count);
                var m = tempReq[pick];
                bool yatayda = rnd.Next(2) == 0;
                bool yerlesti = false;

                if (yatayda)
                {
                    // Yatay duvara eklemeyi dene
                    int s = yatay.Sum(x => x.Item2);
                    int e = s + m.Item2;
                    if (e <= yatayMax)
                    {
                        yatay.Add(m);
                        yerlesti = true;
                    }
                    else
                    {
                        // Yataya sığmıyorsa dikey duvara dene
                        int ds = corner.width2 + dikey.Sum(x => x.Item2);
                        int de = ds + m.Item2;
                        if (de <= dikeyMax)
                        {
                            dikey.Add(m);
                            yerlesti = true;
                        }
                    }
                }
                else
                {
                    // Dikey duvara eklemeyi dene
                    int ds = corner.width2 + dikey.Sum(x => x.Item2);
                    int de = ds + m.Item2;
                    if (de <= dikeyMax)
                    {
                        dikey.Add(m);
                        yerlesti = true;
                    }
                    else
                    {
                        // Dikeye sığmıyorsa yatay duvara dene
                        int s = yatay.Sum(x => x.Item2);
                        int e = s + m.Item2;
                        if (e <= yatayMax)
                        {
                            yatay.Add(m);
                            yerlesti = true;
                        }
                    }
                }

                if (!yerlesti)
                {
                    // Zorunlu modül yerleştirilemedi, başarısız
                    return null;
                }

                tempReq.RemoveAt(pick);
            }

            // 5) Kiler ekleme - rastgele bir konuma eklenir (shuffle sonrası kontrol edilecek)
            bool kilerEklenecekMi = rnd.NextDouble() < 0.7; // %70 şans ile kiler ekle
            if (kilerEklenecekMi)
            {
                int kilerWidth = MODULES["kiler"][rnd.Next(MODULES["kiler"].Count)];

                // Kiler için yer var mı kontrol et
                int yatayKalanAlan = yatayMax - yatay.Sum(x => x.Item2);
                int dikeyKalanAlan = dikeyMax - (corner.width2 + dikey.Sum(x => x.Item2));

                // Rastgele hangi duvara ekleneceğini belirle
                List<int> uygunDuvarlar = new List<int>();
                if (yatayKalanAlan >= kilerWidth) uygunDuvarlar.Add(1); // Duvar 1 (yatay)
                if (dikeyKalanAlan >= kilerWidth) uygunDuvarlar.Add(2); // Duvar 2 (dikey)

                if (uygunDuvarlar.Count > 0)
                {
                    int secilenDuvar = uygunDuvarlar[rnd.Next(uygunDuvarlar.Count)];
                    if (secilenDuvar == 1)
                    {
                        yatay.Add(("kiler", kilerWidth));
                    }
                    else
                    {
                        dikey.Add(("kiler", kilerWidth));
                    }
                }
                // Kilerin sığmadığı durumda eklenmeyecek
            }

            // 6) Kalan boşlukları rastgele modüllerle doldur (dolap, çekmece)
            List<string> rastgeleModuller = new List<string> { "dolap", "cekmece" };
            YatayAlaniDoldurRastgele(yatay, rastgeleModuller, yatay.Sum(x => x.Item2), yatayMax);
            DikeyAlaniDoldurRastgele(dikey, rastgeleModuller, corner.width2 + dikey.Sum(x => x.Item2), dikeyMax, corner.width2);

            // 7) *** ÖNEMLİ: Sıralamaları shuffle et ***
            ShuffleList(yatay);
            ShuffleList(dikey);

            // 8) Shuffle sonrası kiler kontrolü ve pencere kontrolleri
            for (int deneme = 0; deneme < 10; deneme++) // Maksimum 10 deneme
            {
                // Final sonucu oluştur
                List<(string, int)> tempResult = new List<(string, int)>();
                tempResult.AddRange(yatay);
                tempResult.Add((corner.type + "_1", corner.width1));
                tempResult.Add((corner.type + "_2", corner.width2));
                tempResult.AddRange(dikey);

                // Kiler kontrolü
                bool kilerKontrolOK = KilerBuzdolabiYanindaMi(tempResult);

                // Pencere kontrolleri
                bool pencereKontrolOK = PencereKontrolleriniGeciyorMu(yatay, d1Yasak, 1) &&
                                       PencereKontrolleriniGeciyorMu(dikey, d2Yasak, 2, corner.width2);

                if (kilerKontrolOK && pencereKontrolOK)
                {
                    // Debug: Kiler hangi duvarda
                    DebugKilerPozisyon(tempResult);

                    // Her şey uygun, sonucu döndür
                    return tempResult;
                }

                // Başarısızsa tekrar shuffle yap (son deneme değilse)
                if (deneme < 9)
                {
                    ShuffleList(yatay);
                    ShuffleList(dikey);
                }
            }

            // Tüm denemeler başarısızsa kileri çıkarıp tekrar dene
            yatay.RemoveAll(x => x.Item1 == "kiler");
            dikey.RemoveAll(x => x.Item1 == "kiler");

            // Son deneme - sadece pencere kontrolleri
            for (int deneme = 0; deneme < 5; deneme++)
            {
                if (PencereKontrolleriniGeciyorMu(yatay, d1Yasak, 1) &&
                    PencereKontrolleriniGeciyorMu(dikey, d2Yasak, 2, corner.width2))
                {
                    List<(string, int)> result = new List<(string, int)>();
                    result.AddRange(yatay);
                    result.Add((corner.type + "_1", corner.width1));
                    result.Add((corner.type + "_2", corner.width2));
                    result.AddRange(dikey);

                    // Debug: Kiler hangi duvarda (kiler çıkarılmış durumda)
                    DebugKilerPozisyon(result);

                    return result;
                }

                if (deneme < 4)
                {
                    ShuffleList(yatay);
                    ShuffleList(dikey);
                }
            }

            // Hiçbir deneme başarılı olmadıysa null döndür
            return null;
        }

        // Debug için kiler hangi duvarda test fonksiyonu
        private void DebugKilerPozisyon(List<(string, int)> layout)
        {
            int kilerIndex = -1;
            for (int i = 0; i < layout.Count; i++)
            {
                if (layout[i].Item1 == "kiler")
                {
                    kilerIndex = i;
                    break;
                }
            }

            if (kilerIndex == -1)
            {
                Console.WriteLine("Kiler yok");
                return;
            }

            // Corner pozisyonlarını bul
            int corner1Index = -1, corner2Index = -1;
            for (int i = 0; i < layout.Count; i++)
            {
                if (layout[i].Item1.EndsWith("_1"))
                    corner1Index = i;
                else if (layout[i].Item1.EndsWith("_2"))
                    corner2Index = i;
            }

            if (kilerIndex < corner1Index)
            {
                Console.WriteLine($"Kiler DUVAR 1'de - pozisyon: {kilerIndex}");
            }
            else if (kilerIndex > corner2Index)
            {
                Console.WriteLine($"Kiler DUVAR 2'de - pozisyon: {kilerIndex}");
            }
            else
            {
                Console.WriteLine($"Kiler corner bölgesinde? - pozisyon: {kilerIndex}");
            }
        }

        // Liste shuffle metodu
        private void ShuffleList<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                T temp = list[i];
                list[i] = list[j];
                list[j] = temp;
            }
        }

        // Pencere kontrollerini toplu şekilde yapar
        private bool PencereKontrolleriniGeciyorMu(List<(string, int)> modules,
            List<(int, int, string)> yasakliAlanlar, int duvarNo, int cornerOffset = 0)
        {
            int currentPos = 0;
            foreach (var module in modules)
            {
                int startPos = currentPos;
                int endPos = currentPos + module.Item2;

                // Dikey duvar için corner offset'i çıkar
                if (duvarNo == 2)
                {
                    if (PencereIleCarpisiyorMu(startPos, endPos, yasakliAlanlar, module.Item1, duvarNo))
                        return false;
                }
                else
                {
                    if (PencereIleCarpisiyorMu(startPos, endPos, yasakliAlanlar, module.Item1, duvarNo))
                        return false;
                }

                currentPos = endPos;
            }
            return true;
        }

       

        // Basit rastgele modül doldurma metodu (yatay)
        private void YatayAlaniDoldurRastgele(List<(string, int)> yatay, List<string> rastgeleModuller,
            int currentWidth, int maxWidth)
        {
            while (currentWidth < maxWidth)
            {
                string moduleType = rastgeleModuller[rnd.Next(rastgeleModuller.Count)];

                if (!MODULES.ContainsKey(moduleType) || MODULES[moduleType].Count == 0)
                    break;

                int moduleWidth = MODULES[moduleType][rnd.Next(MODULES[moduleType].Count)];

                if (currentWidth + moduleWidth <= maxWidth)
                {
                    yatay.Add((moduleType, moduleWidth));
                    currentWidth += moduleWidth;
                }
                else
                {
                    break;
                }
            }
        }

        // Basit rastgele modül doldurma metodu (dikey)
        private void DikeyAlaniDoldurRastgele(List<(string, int)> dikey, List<string> rastgeleModuller,
            int currentWidth, int maxWidth, int cornerWidth2)
        {
            while (currentWidth < maxWidth)
            {
                string moduleType = rastgeleModuller[rnd.Next(rastgeleModuller.Count)];

                if (!MODULES.ContainsKey(moduleType) || MODULES[moduleType].Count == 0)
                    break;

                int moduleWidth = MODULES[moduleType][rnd.Next(MODULES[moduleType].Count)];

                if (currentWidth + moduleWidth <= maxWidth)
                {
                    dikey.Add((moduleType, moduleWidth));
                    currentWidth += moduleWidth;
                }
                else
                {
                    break;
                }
            }
        }


        // Mevcut kontrol metodunuz
        private bool KilerBuzdolabiYanindaMi(List<(string, int)> layout)
        {
            // Önce kiler var mı kontrol et
            int kilerIndex = -1;
            for (int i = 0; i < layout.Count; i++)
            {
                if (layout[i].Item1 == "kiler")
                {
                    kilerIndex = i;
                    break;
                }
            }

            // Kiler yoksa sorun yok
            if (kilerIndex == -1) return true;

            // Buzdolabını bul
            int buzdolabiIndex = -1;
            for (int i = 0; i < layout.Count; i++)
            {
                if (layout[i].Item1 == "buzdolabi")
                {
                    buzdolabiIndex = i;
                    break;
                }
            }

            // Buzdolabı yoksa (olmaması gereken durum) false döndür
            if (buzdolabiIndex == -1) return false;

            // Corner modüllerinin pozisyonlarını bul
            int corner1Index = -1, corner2Index = -1;
            for (int i = 0; i < layout.Count; i++)
            {
                if (layout[i].Item1.EndsWith("_1"))
                {
                    corner1Index = i;
                }
                else if (layout[i].Item1.EndsWith("_2"))
                {
                    corner2Index = i;
                }
            }

            // Corner modülleri bulunamazsa hata
            if (corner1Index == -1 || corner2Index == -1) return false;

            // Duvar 1: 0'dan corner1Index'e kadar (corner1Index dahil değil)
            // Corner: corner1Index ve corner2Index
            // Duvar 2: corner2Index'ten sonraki tüm elemanlar

            bool kilerDuvar1de = kilerIndex < corner1Index;
            bool buzdolabiDuvar1de = buzdolabiIndex < corner1Index;

            bool kilerDuvar2de = kilerIndex > corner2Index;
            bool buzdolabiDuvar2de = buzdolabiIndex > corner2Index;

            // Kiler ve buzdolabı aynı duvarda mı?
            if (kilerDuvar1de && buzdolabiDuvar1de)
            {
                // İkisi de duvar 1'de - yanyana mı kontrol et
                return Math.Abs(kilerIndex - buzdolabiIndex) == 1;
            }
            else if (kilerDuvar2de && buzdolabiDuvar2de)
            {
                // İkisi de duvar 2'de - yanyana mı kontrol et
                return Math.Abs(kilerIndex - buzdolabiIndex) == 1;
            }
            else
            {
                // Farklı duvarlarda - bu durumda yanyana olamazlar
                return false;
            }
        }

        // Güncellenmiş Uret methodu
        public List<(int skor, List<(string, int)> dizilim, List<string> log)> Uret(int duvar1, int duvar2)
        {
            var sonuc = new List<(int, List<(string, int)>, List<string>)>();

            for (int i = 0; i < 1000000; i++)
            {
                var layout = (duvar2 == 0)
                    ? GenerateTekDuvarDuzeni(duvar1)
                    : GenerateLDuzeni(duvar1, duvar2);

                if (layout == null) continue;

                // Yeni kontrol: Kiler buzdolabının yanında mı?
                if (!KilerBuzdolabiYanindaMi(layout)) continue;

                List<string> log;
                int skor = Evaluate(layout, duvar1 + duvar2, duvar1, duvar2, out log);
                sonuc.Add((skor, layout, log));
            }

            return sonuc.OrderByDescending(x => x.Item1).ToList();
        }


        private bool PencereIleCarpisiyorMu(int modulBaslangic, int modulBitis,
    List<(int basla, int bitir, string sebep)> yasakliAlanlar, string modulTipi,
    int hangiDuvar) // YENİ: hangi duvar parametresi (1 veya 2)
        {
            // Eğer modül pencere altında olabiliyorsa kontrol yapma
            if (modulTipi == "evye" || modulTipi == "bulasik" || modulTipi == "cekmece" || modulTipi == "dolap")
                return false;

            // Hangi duvarın penceresi kontrol edilecek
            bool ilgiliPencereVar = false;
            int ilgiliPencereBaslangic = 0, ilgiliPencereBitis = 0;

            if (hangiDuvar == 1 && pencereVarMi)
            {
                ilgiliPencereVar = true;
                ilgiliPencereBaslangic = pencereBaslangic;
                ilgiliPencereBitis = pencereBitis;
            }
            else if (hangiDuvar == 2 && pencereVarMiD2)
            {
                ilgiliPencereVar = true;
                ilgiliPencereBaslangic = pencereBaslangicD2;
                ilgiliPencereBitis = pencereBitisD2;
            }

            if (!ilgiliPencereVar) return false;

            // Pencere çakışma kontrolü
            if (!(modulBitis <= ilgiliPencereBaslangic || modulBaslangic >= ilgiliPencereBitis))
            {
                return true; // Çakışma var
            }

            // Yasaklı alanlar kontrolü (mevcut mantık)
            foreach (var yasak in yasakliAlanlar)
            {
                if (!(modulBitis <= yasak.basla || modulBaslangic >= yasak.bitir))
                {
                    return true; // Çakışma var
                }
            }

            return false; // Çakışma yok
        }

        private int Evaluate(List<(string, int)> layout, int totalWidth,int duvar1, int duvar2, out List<string> log)
        {
            log = new List<string>();
            int score = 0;

            // Sabit kurallar
            score += EvaluateStaticRules(layout, totalWidth, duvar1, duvar2, log);

            // Kullanıcı kuralları (cache'lenmiş)
            score += EvaluateUserRulesCached(layout, totalWidth, log);

            return score;
        }

        private int EvaluateStaticRules(List<(string, int)> layout, int totalWidth,int duvar1,int duvar2, List<string> log)
        {
            int score = 0;
            var types = layout.Select(x => x.Item1).ToList();
            var widths = layout.Select(x => x.Item2).ToList();

            int idx_fridge = types.FindIndex(t => t.StartsWith("buzdolabi"));
            int idx_sink = types.FindIndex(t => t.StartsWith("evye"));
            int idx_oven = types.FindIndex(t => t.StartsWith("firin"));
            int idx_dishwasher = types.FindIndex(t => t.StartsWith("bulasik"));
            int idx_cellar = types.FindIndex(t => t.StartsWith("kiler"));

            if (idx_fridge != -1 && idx_sink != -1)
            {
                int gapScore = GetGapScore(idx_fridge, idx_sink, widths, 5, 2);
                score += gapScore;
                log.Add($"✔️ Buzdolabı-Evye arası: {(gapScore == 5 ? "≥60cm (+5)" : "<60cm (+2)")}");
            }

            if (idx_oven != -1 && idx_sink != -1)
            {
                int gapScore = GetGapScore(idx_oven, idx_sink, widths, 5, -2);
                score += gapScore;
                log.Add($"✔️ Fırın-Evye arası: {(gapScore == 5 ? "≥60cm (+5)" : "<60cm (+2)")}");
            }

            if (idx_oven != -1 && idx_dishwasher != -1)
            {
                int gapScore = GetGapScore(idx_oven, idx_dishwasher, widths, 6, 3);
                score += gapScore;
                log.Add($"✔️ Fırın-Bulaşık arası: {(gapScore == 6 ? "≥60cm (+6)" : "<60cm (+3)")}");
            }

            if (idx_fridge != -1 && idx_dishwasher != -1)
            {
                int gapScore = GetGapScore(idx_fridge, idx_dishwasher, widths, 6, 3);
                score += gapScore;
                log.Add($"✔️ Buzdolabı-Bulaşık arası: {(gapScore == 6 ? "≥60cm (+6)" : "<60cm (+3)")}");
            }

            if (idx_cellar != -1 && idx_sink != -1)
            {
                int gapScore = GetGapScore(idx_cellar, idx_sink, widths, 6, 3);
                score += gapScore;
                log.Add($"✔️ Kiler-Evye arası: {(gapScore == 6 ? "≥60cm (+6)" : "<60cm (+3)")}");
            }

            if (idx_fridge != -1 && idx_oven != -1)
            {
                int gapScore = GetGapScore(idx_fridge, idx_oven, widths, 8, 4);
                score += gapScore;
                log.Add($"✔️ Buzdolabı-Fırın arası: {(gapScore == 8 ? "≥60cm (+8)" : "<60cm (+4)")}");
            }

            if (idx_dishwasher != -1 && idx_sink != -1)
            {
                int adjacentScore = Math.Abs(idx_dishwasher - idx_sink) == 1 ? 7 : 0;
                score += adjacentScore;
                log.Add($"✔️ Bulaşık-Evye yakınlık: {(adjacentScore == 7 ? "Yan yana (+7)" : "Uzak (+3)")}");
            }
           

            if (idx_fridge != -1 && idx_sink != -1 && idx_oven != -1)
            {
                if (idx_fridge < idx_sink && idx_sink < idx_oven)
                {
                    score += 6;
                    log.Add("✔️ Üçgen sıralama: Buzdolabı-Evye-Fırın (+6)");
                }
                else if (idx_oven < idx_sink && idx_sink < idx_fridge)
                {
                    score += 6;
                    log.Add("✔️ Üçgen sıralama: Fırın-Evye-Buzdolabı (+6)");
                }
                else
                {
                    log.Add("❌ Üçgen sıralama karışık (+0)");
                }
            }

            if (idx_oven != -1)
            {
                bool hasDrawer = (idx_oven > 0 && types[idx_oven - 1] == "cekmece") ||
                                 (idx_oven < types.Count - 1 && types[idx_oven + 1] == "cekmece");
                int drawerScore = hasDrawer ? 7 : 3;
                score += drawerScore;
                log.Add($"✔️ Fırın yanında çekmece: {(hasDrawer ? "Var (+7)" : "Yok (+3)")}");
            }

            if (idx_fridge == 0 ||idx_fridge == types.Count - 1)
            {
                score += 15;
                log.Add("✔️ Buzdolabı pozisyonu: Başta/Sonda (+15)");
                if (duvar2 > 0)
                {
                    if (idx_fridge == 0 && duvar1 > duvar2)
                    {
                        score += 5;
                        log.Add("✔️ Buzdolabı pozisyonu: Uzun duvarda (+5)");
                    }
                    if (idx_fridge == types.Count - 1 && duvar2 > duvar1)
                    {
                        score += 5;
                        log.Add("✔️ Buzdolabı pozisyonu: Uzun duvarda (+5)");
                    }
                }
            }
            else
            {
                log.Add("❌ Buzdolabı pozisyonu: Ortada (+0)");
            }

            int realTotal = CalculateRealTotalWidth(layout);
            int gap = totalWidth - realTotal;

            if (gap == 0)
            {
                score += 15;
                log.Add("✔️ Duvar boşluğu: 0cm (+15)");
            }
            else if (gap <= 10)
            {
                score += 3;
                log.Add($"✔️ Duvar boşluğu: {gap}cm (+3)");
            }
            else if (gap <= 20)
            {
                score += 2;
                log.Add($"✔️ Duvar boşluğu: {gap}cm (+2)");
            }
            else if (gap <= 30)
            {
                score += 1;
                log.Add($"✔️ Duvar boşluğu: {gap}cm (+1)");
            }
            else
            {
                log.Add($"❌ Duvar boşluğu: {gap}cm (+0)");
            }

            return score;
        }

        // Cache'lenmiş kullanıcı kuralları - çok daha hızlı
        private int EvaluateUserRulesCached(List<(string, int)> layout, int totalWidth, List<string> log)
        {
            int totalUserScore = 0;

            foreach (string userRule in userRules)
            {
                try
                {
                    if (compiledRules.ContainsKey(userRule))
                    {
                        int score = compiledRules[userRule](layout, totalWidth);
                        totalUserScore += score;
                        log.Add($"🤖 AI Kuralı [{userRule}]: {(score > 0 ? "+" : "")}{score} puan");
                    }
                    else
                    {
                        log.Add($"❌ Kural bulunamadı: {userRule}");
                    }
                }
                catch (Exception ex)
                {
                    log.Add($"❌ Kullanıcı kuralı hatası [{userRule}]: {ex.Message}");
                }
            }

            return totalUserScore;
        }

        

        private async Task<Func<List<(string, int)>, int, int>> CompileUserRuleAsync(string userRule)
        {
            string prompt = $@"
Create a C# method for kitchen layout rule evaluation. User rule in Turkish: '{userRule}'

Layout data:
- layout: List<(string, int)> - (module_type, width) pairs
- Module types: buzdolabi (fridge), evye (sink), firin (oven), bulasik (dishwasher), cekmece (drawer), dolap (cabinet)
- totalWidth: total wall width in cm

Turkish rule translations:
- 'arası' = between/distance
- 'yan yana' = adjacent/next to each other  
- 'başta' = at the beginning
- 'sonda' = at the end
- 'olmasın' = should not be
- 'yakın' = close/near
- 'uzak' = far

Rules for implementation:
1. Index check: idx != -1 (module exists?)
2. Distance between modules: widths.Skip(Math.Min(idx1, idx2) + 1).Take(Math.Abs(idx1 - idx2) - 1).Sum()
3. Adjacent check: Math.Abs(idx1 - idx2) == 1
4. Position check: idx == 0 (start) or idx == types.Count - 1 (end)
5. Return positive score (5-15) if rule satisfied, negative (-3 to -10) if not

Write ONLY this C# method:

```csharp
public static int EvaluateUserRule(List<(string, int)> layout, int totalWidth)
{{
    var types = layout.Select(x => x.Item1.Replace(""_1"", """").Replace(""_2"", """")).ToList();
    var widths = layout.Select(x => x.Item2).ToList();
    
    // Find indices
    int idx_fridge = types.FindIndex(t => t.StartsWith(""buzdolabi""));
    int idx_sink = types.FindIndex(t => t.StartsWith(""evye""));
    int idx_oven = types.FindIndex(t => t.StartsWith(""firin""));
    int idx_dishwasher = types.FindIndex(t => t.StartsWith(""bulasik""));
    
    // Implement the Turkish rule logic here
    // Example patterns:
    // - For distance: if (spacing >= requiredCm) return 10; else return -5;
    // - For adjacent: if (Math.Abs(idx1 - idx2) == 1) return 12; else return -3;
    // - For position: if (idx == 0 || idx == types.Count - 1) return 10; else return -4;
    
    return 0; // Replace with actual rule implementation
}}
```";

            // Groq'tan kodu al
            string groqResponse = await groqClient.GetResponseFromGroq(prompt);

            if (groqResponse.StartsWith("❌"))
            {
                throw new Exception($"Groq API hatası: {groqResponse}");
            }

            string code = ExtractCSharpCode(groqResponse);
            return CompileRuleToDelegate(code);
        }

        private string ExtractCSharpCode(string groqResponse)
        {
            // İlk olarak ```csharp bloğu içindeki kodu bulmaya çalış
            var match = Regex.Match(groqResponse, @"```csharp\s*(.*?)\s*```", RegexOptions.Singleline);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            // ```c# bloğu dene
            match = Regex.Match(groqResponse, @"```c#\s*(.*?)\s*```", RegexOptions.Singleline);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            // Sadece ``` bloğu dene
            match = Regex.Match(groqResponse, @"```\s*(.*?)\s*```", RegexOptions.Singleline);
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            // Eğer bulamazsa, public static int ile başlayan metodu ara
            var methodMatch = Regex.Match(groqResponse, @"(public static int EvaluateUserRule.*?^\})", RegexOptions.Singleline | RegexOptions.Multiline);
            if (methodMatch.Success)
            {
                return methodMatch.Groups[1].Value.Trim();
            }

            throw new Exception("Groq yanıtından geçerli C# kodu çıkarılamadı. Yanıt: " + groqResponse.Substring(0, Math.Min(200, groqResponse.Length)));
        }

        private Func<List<(string, int)>, int, int> CompileRuleToDelegate(string methodCode)
        {
            string fullCode = $@"
using System;
using System.Collections.Generic;
using System.Linq;

public class UserRule
{{
    {methodCode}
}}";

            try
            {
                // Roslyn ile derleme
                SyntaxTree syntaxTree = CSharpSyntaxTree.ParseText(fullCode);

                var references = new List<MetadataReference>
                {
                    MetadataReference.CreateFromFile(typeof(object).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(Enumerable).Assembly.Location),
                    MetadataReference.CreateFromFile(typeof(List<>).Assembly.Location)
                };

                try
                {
                    references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Runtime").Location));
                    references.Add(MetadataReference.CreateFromFile(Assembly.Load("System.Collections").Location));
                }
                catch
                {
                    references.Add(MetadataReference.CreateFromFile(typeof(System.Runtime.CompilerServices.DynamicAttribute).Assembly.Location));
                }

                CSharpCompilation compilation = CSharpCompilation.Create(
                    $"UserRuleAssembly_{Guid.NewGuid():N}",
                    new[] { syntaxTree },
                    references,
                    new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

                using (var ms = new MemoryStream())
                {
                    EmitResult result = compilation.Emit(ms);

                    if (!result.Success)
                    {
                        var failures = result.Diagnostics.Where(diagnostic =>
                            diagnostic.IsWarningAsError ||
                            diagnostic.Severity == DiagnosticSeverity.Error);

                        string errors = string.Join("\n", failures.Select(f => f.GetMessage()));
                        throw new Exception($"Kod derleme hatası:\n{errors}");
                    }

                    ms.Seek(0, SeekOrigin.Begin);
                    Assembly assembly = Assembly.Load(ms.ToArray());
                    Type type = assembly.GetType("UserRule");
                    MethodInfo method = type.GetMethod("EvaluateUserRule");

                    // Delegate oluştur - çok daha hızlı
                    return (layout, totalWidth) => (int)method.Invoke(null, new object[] { layout, totalWidth });
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Kod derleme hatası: {ex.Message}");
            }
        }

        private int CalculateRealTotalWidth(List<(string, int)> layout)
        {
            int realTotal = 0;

            for (int i = 0; i < layout.Count; i++)
            {
                // _2 parçalarını atla (zaten _1 ile birlikte hesaplanacak)
                if (layout[i].Item1.Contains("_2")) continue;

                if (layout[i].Item1.Contains("_1"))
                {
                    // Köşe modülü - width1 + width2 toplamı
                    string type = layout[i].Item1.Replace("_1", "");
                    var kose = CORNER_MODULES.First(x => x.type == type);
                    realTotal += kose.width1 + kose.width2;
                }
                else
                {
                    // Normal modül
                    realTotal += layout[i].Item2;
                }
            }

            return realTotal;
        }

        
        private int GetGapScore(int idx1, int idx2, List<int> widths, int good, int bad)
        {
            int spacing = widths.Skip(Math.Min(idx1, idx2) + 1)
                                .Take(Math.Abs(idx1 - idx2) - 1)
                                .Sum();
            return spacing >= 60 ? good : bad;
        }

        private List<(string, int)> GenerateTekDuvarDuzeni(int duvar)
        {
            // Pencere yasaklı alanları
            List<(int, int, string)> duvar1Yasakli;
            List<(int, int, string)> ignore;
            GetDuvarYasakliAlanlari(duvar, 0, out duvar1Yasakli, out ignore);

            // Zorunlu modüller (evye, fırın, bulaşık, buzdolabı)
            List<string> reqTypes = new List<string> { "evye", "firin", "bulasik", "buzdolabi" };
            List<(string, int)> required = new List<(string, int)>();
            foreach (string t in reqTypes)
                required.Add((t, MODULES[t][rnd.Next(MODULES[t].Count)]));

            // Kiler (opsiyonel, maksimum 1 tane)
            double kilerSansi = rnd.NextDouble();
            bool kilerEklenecekMi = kilerSansi < 0.7;
            Console.WriteLine($"Kiler şansı: {kilerSansi:F2}, Eklenecek mi: {kilerEklenecekMi}");

            int cellarWidth = 0;
            if (kilerEklenecekMi)
            {
                if (MODULES["kiler"].Count == 0)
                {
                    Console.WriteLine("HATA: MODULES['kiler'] boş!");
                    kilerEklenecekMi = false;
                }
                else
                {
                    cellarWidth = MODULES["kiler"][rnd.Next(MODULES["kiler"].Count)];
                    Console.WriteLine($"Seçilen kiler genişliği: {cellarWidth}");
                }
            }

            List<(string, int)> layout = new List<(string, int)>();
            List<(string, int)> tempReq = new List<(string, int)>(required);

            // Zorunlu modülleri basit şekilde yerleştir (sona ekleme)
            int pos = 0;
            while (tempReq.Count > 0)
            {
                int pick = rnd.Next(tempReq.Count);
                var m = tempReq[pick];

                if (pos + m.Item2 > duvar)
                {
                    // Modül sığmıyor, başarısız
                    return null;
                }

                layout.Add(m);
                pos += m.Item2;
                tempReq.RemoveAt(pick);
            }

            Console.WriteLine($"Zorunlu modüller yerleştirildi. Mevcut pozisyon: {pos}, Duvar genişliği: {duvar}");

            // Kiler ekleme (rastgele konuma, shuffle sonrası kontrol edilecek)
            if (kilerEklenecekMi)
            {
                Console.WriteLine($"Kiler ekleme kontrolü: pos({pos}) + cellarWidth({cellarWidth}) = {pos + cellarWidth} <= duvar({duvar}) = {pos + cellarWidth <= duvar}");

                if (pos + cellarWidth <= duvar)
                {
                    layout.Add(("kiler", cellarWidth));
                    pos += cellarWidth;
                    Console.WriteLine($"✓ Kiler başarıyla eklendi! Yeni pozisyon: {pos}");
                }
                else
                {
                    Console.WriteLine($"✗ Kiler sığmadı! Gerekli alan: {pos + cellarWidth}, Mevcut duvar: {duvar}");
                }
            }
            else
            {
                Console.WriteLine("Kiler eklenmeyecek (şans %70'e denk gelmedi)");
            }

            // Kalan boşlukları rastgele modüllerle doldur
            List<string> rastgeleModuller = new List<string> { "dolap", "cekmece" };
            TekDuvarAlaniDoldurRastgele(layout, rastgeleModuller, pos, duvar, duvar1Yasakli);

            // *** ÖNEMLİ: Sıralamayı shuffle et ***
            Console.WriteLine("Shuffle öncesi: " + string.Join(", ", layout.Select(x => x.Item1)));
            ShuffleList(layout);
            Console.WriteLine("Shuffle sonrası: " + string.Join(", ", layout.Select(x => x.Item1)));

            // Shuffle sonrası kiler ve pencere kontrolleri
            for (int deneme = 0; deneme < 10; deneme++)
            {
                // Kiler kontrolü
                bool kilerKontrolOK = KilerBuzdolabiYanindaMiTekDuvar(layout);

                // Pencere kontrolleri
                bool pencereKontrolOK = PencereKontrolleriniGeciyorMu(layout, duvar1Yasakli, 1);

                Console.WriteLine($"Deneme {deneme + 1}: Kiler kontrol: {kilerKontrolOK}, Pencere kontrol: {pencereKontrolOK}");

                if (kilerKontrolOK && pencereKontrolOK)
                {
                    // Debug: Kiler hangi pozisyonda
                    DebugKilerPozisyonTekDuvar(layout);

                    return layout;
                }

                // Başarısızsa tekrar shuffle yap (son deneme değilse)
                if (deneme < 9)
                {
                    ShuffleList(layout);
                }
            }

            // Tüm denemeler başarısızsa kileri çıkarıp tekrar dene
            Console.WriteLine("Tüm denemeler başarısız, kiler çıkarılıyor");
            layout.RemoveAll(x => x.Item1 == "kiler");

            // Son deneme - sadece pencere kontrolleri
            for (int deneme = 0; deneme < 5; deneme++)
            {
                if (PencereKontrolleriniGeciyorMu(layout, duvar1Yasakli, 1))
                {
                    // Debug: Kiler çıkarıldı
                    DebugKilerPozisyonTekDuvar(layout);

                    return layout;
                }

                if (deneme < 4)
                {
                    ShuffleList(layout);
                }
            }

            // Hiçbir deneme başarılı olmadıysa null döndür
            return null;
        }

        // Tek duvar için kiler pozisyon debug fonksiyonu
        private void DebugKilerPozisyonTekDuvar(List<(string, int)> layout)
        {
            int kilerIndex = -1;
            int buzdolabiIndex = -1;

            for (int i = 0; i < layout.Count; i++)
            {
                if (layout[i].Item1 == "kiler")
                    kilerIndex = i;
                else if (layout[i].Item1 == "buzdolabi")
                    buzdolabiIndex = i;
            }

            if (kilerIndex == -1)
            {
                Console.WriteLine("TEK DUVAR: Kiler yok");
            }
            else if (buzdolabiIndex == -1)
            {
                Console.WriteLine("TEK DUVAR: Buzdolabı bulunamadı!");
            }
            else
            {
                bool yanYana = Math.Abs(kilerIndex - buzdolabiIndex) == 1;
                Console.WriteLine($"TEK DUVAR: Kiler pozisyon {kilerIndex}, Buzdolabı pozisyon {buzdolabiIndex}, Yanyana: {yanYana}");
            }
        }

        // Tek duvar için basitleştirilmiş kiler kontrolü
        private bool KilerBuzdolabiYanindaMiTekDuvar(List<(string, int)> layout)
        {
            // Önce kiler var mı kontrol et
            int kilerIndex = -1;
            for (int i = 0; i < layout.Count; i++)
            {
                if (layout[i].Item1 == "kiler")
                {
                    kilerIndex = i;
                    break;
                }
            }

            // Kiler yoksa sorun yok
            if (kilerIndex == -1) return true;

            // Buzdolabını bul
            int buzdolabiIndex = -1;
            for (int i = 0; i < layout.Count; i++)
            {
                if (layout[i].Item1 == "buzdolabi")
                {
                    buzdolabiIndex = i;
                    break;
                }
            }

            // Buzdolabı yoksa (olmaması gereken durum) false döndür
            if (buzdolabiIndex == -1) return false;

            // Kiler buzdolabının hemen yanında mı kontrol et (solunda veya sağında)
            return Math.Abs(kilerIndex - buzdolabiIndex) == 1;
        }

        private void TekDuvarAlaniDoldurRastgele(List<(string, int)> layout, List<string> rastgeleModuller,
    int mevcutPozisyon, int maxPozisyon, List<(int, int, string)> yasakliAlanlar)
        {
            while (mevcutPozisyon < maxPozisyon)
            {
                string seciliModul = rastgeleModuller[rnd.Next(rastgeleModuller.Count)];
                int seciliGenislik = MODULES[seciliModul][rnd.Next(MODULES[seciliModul].Count)];

                int yeniPozisyon = mevcutPozisyon + seciliGenislik;
                if (yeniPozisyon > maxPozisyon) break;

                if (!PencereIleCarpisiyorMu(mevcutPozisyon, yeniPozisyon, yasakliAlanlar, seciliModul, 1))
                {
                    layout.Add((seciliModul, seciliGenislik));
                    mevcutPozisyon = yeniPozisyon;
                }
                else
                {
                    break; // Pencere ile çakışıyor, dur
                }
            }
        }



        public void Dispose()
        {
            groqClient?.Dispose();
        }
    }
}