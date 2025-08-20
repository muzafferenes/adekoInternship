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
        public List<(int skor, List<(string, int)> dizilim, List<string> log)> Uret(int duvar1, int duvar2)
        {
            var sonuc = new List<(int, List<(string, int)>, List<string>)>();
            for (int i = 0; i < 1000000; i++)
            {
                var layout = (duvar2 == 0)
                    ? GenerateTekDuvarDuzeni(duvar1)
                    : GenerateLDuzeni(duvar1, duvar2);

                if (layout == null) continue;

                List<string> log;
                int skor = Evaluate(layout, duvar1 + duvar2, duvar1, duvar2, out log);
                sonuc.Add((skor, layout, log));
            }
            return sonuc.OrderByDescending(x => x.Item1).ToList();
        }







        private List<(string, int)> GenerateLDuzeni(int duvar1, int duvar2)
        {
            // 1) Pencere yasaklı alanlarını iki duvara ayır
            List<(int, int, string)> d1Yasak, d2Yasak;
            GetDuvarYasakliAlanlari(duvar1, duvar2, out d1Yasak, out d2Yasak);

            // 2) Zorunlu modüller (evye‑fırın‑bulaşık) rastgele sıralanır
            List<string> reqTypes = new List<string> { "evye", "firin", "bulasik" };
            for (int i = reqTypes.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                string tmp = reqTypes[i]; reqTypes[i] = reqTypes[j]; reqTypes[j] = tmp;
            }
            List<(string, int)> required = new List<(string, int)>();
            foreach (string t in reqTypes)
                required.Add((t, MODULES[t][rnd.Next(MODULES[t].Count)]));

            // 3) Buzdolabı ve kiler genişlikleri
            int fridgeWidth = MODULES["buzdolabi"][rnd.Next(MODULES["buzdolabi"].Count)];
            int cellarWidth = MODULES["kiler"][rnd.Next(MODULES["kiler"].Count)];

            // 4) Senaryolar: 0 → buzdolabı yatay başta, 1 → dikey sonda
            List<int> scenarios = new List<int>();
            int ilkSenaryo = rnd.Next(2); // 0 veya 1 rastgele seç
            scenarios.Add(ilkSenaryo);    // İlk tercih
            scenarios.Add(1 - ilkSenaryo); // Alternatif

            foreach (int senaryo in scenarios)
            {
                List<(string, int)> yatay = new List<(string, int)>();
                List<(string, int)> dikey = new List<(string, int)>();
                List<(string, int)> tempReq = new List<(string, int)>(required);

                var corner = CORNER_MODULES[rnd.Next(CORNER_MODULES.Count)];
                int yatayMax = duvar1 - corner.width1;
                int dikeyMax = duvar2;
                bool ok = true;

                // --- buzdolabı başta senaryosu ---
                if (senaryo == 0)
                {
                    if (fridgeWidth > yatayMax ||
                        PencereIleCarpisiyorMu(0, fridgeWidth, d1Yasak, "buzdolabi", 1))
                    {
                        ok = false;
                    }
                    else
                    {
                        yatay.Add(("buzdolabi", fridgeWidth));
                        if (rnd.NextDouble() < 0.6)
                        {
                            int end = fridgeWidth + cellarWidth;
                            if (end <= yatayMax &&
                                !PencereIleCarpisiyorMu(fridgeWidth, end, d1Yasak, "kiler", 1))
                                yatay.Add(("kiler", cellarWidth));
                        }
                    }
                }

                // --- zorunlu modülleri serbest yerleştir ---
                while (ok && tempReq.Count > 0)
                {
                    int pick = rnd.Next(tempReq.Count);
                    var m = tempReq[pick];
                    bool yatayda = rnd.Next(2) == 0;
                    bool yerlesti = false;

                    if (yatayda)
                    {
                        int s = yatay.Sum(x => x.Item2);
                        int e = s + m.Item2;
                        if (e <= yatayMax && !PencereIleCarpisiyorMu(s, e, d1Yasak, m.Item1, 1))
                        {
                            yatay.Add(m); yerlesti = true;
                        }
                        else
                        {
                            int ds = corner.width2 + dikey.Sum(x => x.Item2);
                            int de = ds + m.Item2;
                            if (de <= dikeyMax &&
                                !PencereIleCarpisiyorMu(ds - corner.width2, de - corner.width2, d2Yasak, m.Item1, 2))
                            {
                                dikey.Add(m); yerlesti = true;
                            }
                        }
                    }
                    else
                    {
                        int ds = corner.width2 + dikey.Sum(x => x.Item2);
                        int de = ds + m.Item2;
                        if (de <= dikeyMax &&
                            !PencereIleCarpisiyorMu(ds - corner.width2, de - corner.width2, d2Yasak, m.Item1, 2))
                        {
                            dikey.Add(m); yerlesti = true;
                        }
                        else
                        {
                            int s = yatay.Sum(x => x.Item2);
                            int e = s + m.Item2;
                            if (e <= yatayMax && !PencereIleCarpisiyorMu(s, e, d1Yasak, m.Item1, 1))
                            {
                                yatay.Add(m); yerlesti = true;
                            }
                        }
                    }

                    if (!yerlesti) { ok = false; break; }
                    tempReq.RemoveAt(pick);
                }
                if (!ok) continue;

                // --- buzdolabı dikey sonda senaryosu ---
                if (senaryo == 1)
                {
                    int ds = corner.width2 + dikey.Sum(x => x.Item2);
                    int de = ds + fridgeWidth;
                    if (de > dikeyMax ||
                        PencereIleCarpisiyorMu(ds - corner.width2, de - corner.width2, d2Yasak, "buzdolabi", 2))
                        continue;

                    if (rnd.NextDouble() < 0.6)
                    {
                        int ks = ds;
                        int ke = ks + cellarWidth;
                        if (ke + fridgeWidth <= dikeyMax &&
                            !PencereIleCarpisiyorMu(ks - corner.width2, ke - corner.width2, d2Yasak, "kiler", 2) &&
                            !PencereIleCarpisiyorMu(ke - corner.width2, ke - corner.width2 + fridgeWidth, d2Yasak, "buzdolabi", 2))
                        {
                            dikey.Add(("kiler", cellarWidth));
                            dikey.Add(("buzdolabi", fridgeWidth));
                        }
                        else
                        {
                            dikey.Add(("buzdolabi", fridgeWidth));
                        }
                    }
                    else
                    {
                        dikey.Add(("buzdolabi", fridgeWidth));
                    }
                }

                // --- kalan boşlukları doldur ---
                YatayAlaniDoldurDuzgun(yatay, new List<(string, int)>(),
                    yatay.Sum(x => x.Item2), yatayMax, d1Yasak, true);
                DikeyAlaniDoldurDuzgun(dikey, new List<(string, int)>(),
                    corner.width2 + dikey.Sum(x => x.Item2), dikeyMax, d2Yasak, true);

                // --- parçaları birleştir ---
                List<(string, int)> result = new List<(string, int)>();
                result.AddRange(yatay);
                result.Add((corner.type + "_1", corner.width1));
                result.Add((corner.type + "_2", corner.width2));
                result.AddRange(dikey);

                // DÜZELTME: Buzdolabının doğru pozisyonda olup olmadığını kontrol et
                int idxFr = result.FindIndex(x => x.Item1.StartsWith("buzdolabi"));
                int cornerStartIdx = result.FindIndex(x => x.Item1.Contains("_1"));

                if (senaryo == 0)
                {
                    // Senaryo 0: Buzdolabı yatay kısımda (duvar1) olmalı
                    // Yani köşe modülünden ÖNCE gelmeli
                    if (idxFr >= 0 && idxFr < cornerStartIdx)
                    {
                        return result;
                    }
                }
                else if (senaryo == 1)
                {
                    // Senaryo 1: Buzdolabı dikey kısımda (duvar2) olmalı
                    // Yani köşe modülünden SONRA gelmeli
                    if (idxFr > cornerStartIdx + 1) // +1 çünkü köşe modülü _1 ve _2 olmak üzere 2 parça
                    {
                        return result;
                    }
                }

                // Bu senaryo başarısız, diğerini dene
            }

            return null; // Hiçbir senaryo başarılı olamadı
        }



        private void YatayAlaniDoldurDuzgun(
            List<(string, int)> moduller,
            List<(string, int)> kaynakModuller,
            int baslangicPoz, int maxPoz,
            List<(int, int, string)> yasakliAlanlar,
            bool ekstraEkle)
        {
            int pozisyon = baslangicPoz;

            // Required'ları (varsa) yerleştir – sığmayanı silme
            while (kaynakModuller.Any())
            {
                int pick = rnd.Next(kaynakModuller.Count);
                var m = kaynakModuller[pick];

                if (pozisyon + m.Item2 <= maxPoz)
                {
                    // Required’lar için insert değil, sona ekle (sıra bozulmasın)
                    moduller.Add(m);
                    pozisyon += m.Item2;
                }
                // denendi, sıradaki adaya geç
                kaynakModuller.RemoveAt(pick);
            }

            if (!ekstraEkle) return;

            // --- ÇİFT KORUMA AYARLARI ---
            // buzdolabı-kiler yan yana ise aralarına eklemeyi yasakla
            int idxFr = moduller.FindIndex(x => x.Item1.StartsWith("buzdolabi"));
            int idxKi = moduller.FindIndex(x => x.Item1.StartsWith("kiler"));
            int blockBetween = -1; // yasaklı sınır (insertion boundary)
            if (idxFr != -1 && idxKi != -1 && Math.Abs(idxFr - idxKi) == 1)
                blockBetween = Math.Min(idxFr, idxKi) + 1;

            bool fridgeAtHead = (idxFr == 0); // senaryo==0 sağlığı için 0'a ekleme yapma

            // Ekstra (çekmece/dolap) – boşluk kaldıkça
            while (pozisyon < maxPoz - 40)
            {
                string extra = rnd.Next(2) == 0 ? "cekmece" : "dolap";
                int w = MODULES[extra][rnd.Next(MODULES[extra].Count)];
                if (pozisyon + w > maxPoz) break;

                // izinli insert konumlarını topla
                var allowed = Enumerable.Range(0, moduller.Count + 1).ToList();

                // buzdolabı başta ise 0'a ekleme → bozar
                if (fridgeAtHead) allowed.Remove(0);

                // buzdolabı-kiler çifti arasını koru
                if (blockBetween != -1) allowed.Remove(blockBetween);

                // hiç yer kalmazsa append etme, döngüden çık
                if (allowed.Count == 0) break;

                int insertAt = allowed[rnd.Next(allowed.Count)];
                moduller.Insert(insertAt, (extra, w));
                pozisyon += w;
            }
        }




        // Dikey için aynı strateji
        // GELİŞTİRİLMİŞ DİKEY DOLDURMA – çift koruma + sonu koruma
        private void DikeyAlaniDoldurDuzgun(
            List<(string, int)> moduller,
            List<(string, int)> kaynakModuller,
            int baslangicPoz, int maxPoz,
            List<(int, int, string)> yasakliAlanlar,
            bool ekstraEkle)
        {
            int pozisyon = baslangicPoz;

            // Required'ları (varsa) yerleştir – sığmayanı silme
            while (kaynakModuller.Any())
            {
                int pick = rnd.Next(kaynakModuller.Count);
                var m = kaynakModuller[pick];

                if (pozisyon + m.Item2 <= maxPoz)
                {
                    moduller.Add(m); // required’lar için append
                    pozisyon += m.Item2;
                }
                kaynakModuller.RemoveAt(pick);
            }

            if (!ekstraEkle) return;

            // --- ÇİFT KORUMA AYARLARI ---
            int idxFr = moduller.FindIndex(x => x.Item1.StartsWith("buzdolabi"));
            int idxKi = moduller.FindIndex(x => x.Item1.StartsWith("kiler"));
            int blockBetween = -1;
            if (idxFr != -1 && idxKi != -1 && Math.Abs(idxFr - idxKi) == 1)
                blockBetween = Math.Min(idxFr, idxKi) + 1;

            bool fridgeAtTail = (idxFr == moduller.Count - 1 && idxFr != -1); // senaryo==1 sağlığı için sona ekleme yok

            while (pozisyon < maxPoz - 40)
            {
                string extra = rnd.Next(2) == 0 ? "cekmece" : "dolap";
                int w = MODULES[extra][rnd.Next(MODULES[extra].Count)];
                if (pozisyon + w > maxPoz) break;

                var allowed = Enumerable.Range(0, moduller.Count + 1).ToList();

                // buzdolabı en sonda ise, sondan sonra ekleme (Count) yasak
                if (fridgeAtTail) allowed.Remove(moduller.Count);

                // buzdolabı-kiler çiftinin arasına ekleme
                if (blockBetween != -1) allowed.Remove(blockBetween);

                if (allowed.Count == 0) break;

                int insertAt = allowed[rnd.Next(allowed.Count)];
                moduller.Insert(insertAt, (extra, w));
                pozisyon += w;
            }
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
            if (idx_cellar != -1 && idx_fridge != -1)
            {
                int adjacentScore = Math.Abs(idx_cellar - idx_fridge) == 1 ? 7 : 0;
                score += adjacentScore;
                log.Add($"✔️ Kiler-Buzdolabı yakınlık: {(adjacentScore == 7 ? "Yan yana (+7)" : "Uzak (+3)")}");
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

            if (idx_fridge == 0 || idx_fridge == types.Count - 1)
            {
                score += 15;
                log.Add("✔️ Buzdolabı pozisyonu: Başta/Sonda (+15)");
                if (idx_fridge == 0 && duvar1 >duvar2)
                {
                    score += 5;
                    log.Add("✔️ Buzdolabı pozisyonu: Uzun duvarda (+5)");
                }
                if(idx_fridge == types.Count - 1 && duvar2 > duvar1)
                {
                    score += 5;
                    log.Add("✔️ Buzdolabı pozisyonu: Uzun duvarda (+5)");
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

        private List<(string, int)> GenerateTekDuvarDuzeniInternal(int duvar, bool fridgeAtStart)
        {
            // Pencere yasaklı alanları
            List<(int, int, string)> duvar1Yasakli;
            List<(int, int, string)> ignore;
            GetDuvarYasakliAlanlari(duvar, 0, out duvar1Yasakli, out ignore);

            // Zorunlu modüller
            List<string> reqTypes = new List<string> { "evye", "firin", "bulasik" };
            for (int i = reqTypes.Count - 1; i > 0; i--)
            {
                int j = rnd.Next(i + 1);
                string tmp = reqTypes[i]; reqTypes[i] = reqTypes[j]; reqTypes[j] = tmp;
            }
            List<(string, int)> required = new List<(string, int)>();
            foreach (string t in reqTypes)
                required.Add((t, MODULES[t][rnd.Next(MODULES[t].Count)]));

            // Buzdolabı ve kiler (opsiyonel)
            int fridgeWidth = MODULES["buzdolabi"][rnd.Next(MODULES["buzdolabi"].Count)];
            int cellarWidth = MODULES["kiler"][rnd.Next(MODULES["kiler"].Count)];

            List<(string, int)> layout = new List<(string, int)>();
            int pos = 0;

            // Buzdolabı başta
            if (fridgeAtStart &&
                !PencereIleCarpisiyorMu(0, fridgeWidth, duvar1Yasakli, "buzdolabi", 1) &&
                fridgeWidth <= duvar)
            {
                layout.Add(("buzdolabi", fridgeWidth));
                pos += fridgeWidth;

                if (rnd.NextDouble() < 0.6 &&
                    pos + cellarWidth <= duvar &&
                    !PencereIleCarpisiyorMu(pos, pos + cellarWidth, duvar1Yasakli, "kiler", 1))
                {
                    layout.Add(("kiler", cellarWidth));
                }
                pos += layout.Count > 1 ? cellarWidth : 0;
            }

            // Zorunlu modüller
            foreach (var m in required)
            {
                if (pos + m.Item2 > duvar ||
                    PencereIleCarpisiyorMu(pos, pos + m.Item2, duvar1Yasakli, m.Item1, 1))
                    return null;

                layout.Add(m);
                pos += m.Item2;
            }

            // Buzdolabı sonda
            if (!fridgeAtStart)
            {
                if (pos + fridgeWidth > duvar ||
                    PencereIleCarpisiyorMu(pos, pos + fridgeWidth, duvar1Yasakli, "buzdolabi", 1))
                    return null;

                layout.Add(("buzdolabi", fridgeWidth));
                pos += fridgeWidth;

                if (rnd.NextDouble() < 0.6 &&
                    pos + cellarWidth <= duvar &&
                    !PencereIleCarpisiyorMu(pos, pos + cellarWidth, duvar1Yasakli, "kiler", 1))
                {
                    layout.Add(("kiler", cellarWidth));
                    pos += cellarWidth;
                }
            }

            // Kalan boşluklar
            YatayAlaniDoldurDuzgun(layout, new List<(string, int)>(), pos, duvar, duvar1Yasakli, true);

            return layout;
        }

        private List<(string, int)> GenerateTekDuvarDuzeni(int duvar)
        {
            // İlk tercih rastgele %50-50
            bool ilkTercih = rnd.Next(2) == 0;

            // İlk tercihi dene
            var result = GenerateTekDuvarDuzeniInternal(duvar, ilkTercih);
            if (result != null) return result;

            // Başarısız olursa karşıt pozisyonu dene
            result = GenerateTekDuvarDuzeniInternal(duvar, !ilkTercih);
            return result;
        }

        public void Dispose()
        {
            groqClient?.Dispose();
        }
    }
}