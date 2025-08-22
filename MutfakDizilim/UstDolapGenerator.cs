using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace MutfakDizilim
{
    internal class UstDolapGenerator
    {
        private readonly Random rnd = new Random();
        private readonly GroqClient groqClient = new GroqClient(); // Groq API client
        private List<string> userRules = new List<string>();

        // Derlenmiş kuralları cache'lemek için
        private Dictionary<string, Func<List<(string, int)>, int, int>> compiledRules =
            new Dictionary<string, Func<List<(string, int)>, int, int>>();

        // DÜZELTME: Sadece dolap modülleri
        private Dictionary<string, List<int>> MODULES = new Dictionary<string, List<int>>
        {
            { "dolap", new List<int> {40, 45, 50, 55, 60, 65, 70, 80, 90, 100} }
        };

        // DÜZELTME: Üst dolap köşe modülleri - küçük boyutlar
        private List<(string type, int width1, int width2)> CORNER_MODULES = new List<(string, int, int)>
        {
            ("ust_kose60x60", 60, 60),
            ("ust_kose65x65", 65, 65)
        };
        // YENİ: Pencere konumu bilgileri
        private int pencereBaslangic = -1;
        private int pencereBitis = -1;
        private bool pencereVarMi = false;

        // YENİ: Duvar2 pencere değişkenleri
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


        private (int kilerBaslangic, int kilerBitis, bool duvar1de, bool duvar2de)
HesaplaKilerPozisyonu(List<(string, int)> altDizilim, int duvar1, int duvar2,
    (string type, int width1, int width2) ustKose)
        {
            if (altDizilim == null) return (-1, -1, false, false);

            int totalPos = 0;
            bool duvar2Basladi = false;
            int altKoseWidth2 = 0;

            foreach (var modul in altDizilim)
            {
                if (modul.Item1.Contains("kose"))
                {
                    if (modul.Item1.EndsWith("_1"))
                    {
                        totalPos += modul.Item2;
                        continue;
                    }
                    if (modul.Item1.EndsWith("_2"))
                    {
                        altKoseWidth2 = modul.Item2;
                        totalPos += modul.Item2;
                        duvar2Basladi = true;
                        continue;
                    }
                }

                if (modul.Item1.Contains("kiler"))
                {
                    int basAlt = totalPos;
                    int bitAlt = totalPos + modul.Item2;

                    if (!duvar2Basladi)
                    {
                        return (basAlt, bitAlt, true, false);
                    }
                    else
                    {
                        int koseFarki = altKoseWidth2 - ustKose.width2;
                        int basUst = basAlt - koseFarki;
                        int bitUst = bitAlt - koseFarki;
                        return (basUst, bitUst, false, true);
                    }
                }

                totalPos += modul.Item2;
            }

            return (-1, -1, false, false);
        }



        
   private (int buzdolabiBaslangic, int buzdolabiBitis, bool duvar1de, bool duvar2de)
HesaplaBuzdolabiPozisyonu(List<(string, int)> altDizilim, int duvar1, int duvar2,
    (string type, int width1, int width2) ustKose)
        {
            if (altDizilim == null) return (-1, -1, false, false);

            int totalPos = 0;        // ALT yerleşimin global ilerleyişi
            bool duvar2Basladi = false;
            int altKoseWidth2 = 0;

            foreach (var modul in altDizilim)
            {
                if (modul.Item1.Contains("kose"))
                {
                    if (modul.Item1.EndsWith("_1"))
                    {
                        totalPos += modul.Item2;   // duvar-1 kolu
                        continue;
                    }
                    if (modul.Item1.EndsWith("_2"))
                    {
                        altKoseWidth2 = modul.Item2; // duvar-2 kolu (ALT)
                        totalPos += modul.Item2;     // global ilerleyişe dahil
                        duvar2Basladi = true;
                        continue;
                    }
                }

                if (modul.Item1.Contains("buzdolabi"))
                {
                    int basAlt = totalPos;
                    int bitAlt = totalPos + modul.Item2;

                    if (!duvar2Basladi)
                    {
                        // DUVAR-1: alt ve üst aynı referans
                        return (basAlt, bitAlt, true, false);
                    }
                    else
                    {
                        // DUVAR-2: ALT → ÜST dönüşüm (üst köşe ile hizala)
                        int koseFarki = altKoseWidth2 - ustKose.width2;
                        int basUst = basAlt - koseFarki;
                        int bitUst = bitAlt - koseFarki;
                        return (basUst, bitUst, false, true);
                    }
                }

                totalPos += modul.Item2; // sıradan modül
            }

            return (-1, -1, false, false);
        }


        // Buzdolabı boşluklu dizilim metodu (pozisyon temelli)
        private List<(string, int)> BuzdolabiBoslukluDizilim(int hedefGenislik,
            int buzdolabiBaslangic, int buzdolabiBitis, bool duvar1de = false)
        {
            var moduller = new List<(string, int)>();
            int buzdolabiGenislik = buzdolabiBitis - buzdolabiBaslangic;

            // Buzdolabı başlangıcına kadar dolap
            if (buzdolabiBaslangic > 0)
            {
                var oncesiDolaplar = NormalUstDolapDizilimi(buzdolabiBaslangic);
                moduller.AddRange(oncesiDolaplar);
            }

            // Buzdolabı boşluğu
            moduller.Add(("bosluk", buzdolabiGenislik));

            // Buzdolabı sonrası kalan alan
            int kalanAlan = hedefGenislik - buzdolabiBitis;
            if (kalanAlan > 0)
            {
                var sonrasiDolaplar = NormalUstDolapDizilimi(kalanAlan);
                moduller.AddRange(sonrasiDolaplar);
            }

            return moduller;
        }


        private (int pencereBaslangic, int pencereBitis, bool duvar1de, bool duvar2de,int pencereBaslangicD2, int pencereBitisD2) HesaplaPencerePozisyonu(
    int duvar1, int duvar2, (string type, int width1, int width2) ustKose)
        {
            // Duvar 1 pencere hesaplaması (mevcut mantık)
            bool duvar1dePencere = false;
            int d1PencereBaslangic = 0, d1PencereBitis = 0;

            if (pencereVarMi)
            {
                if (pencereBitis <= duvar1)
                {
                    // Pencere sadece DUVAR1'de
                    d1PencereBaslangic = pencereBaslangic;
                    d1PencereBitis = pencereBitis;
                    duvar1dePencere = true;
                }
            }

            
            bool duvar2dePencere = false;
            int d2PencereBaslangic = 0, d2PencereBitis = 0;

            if (pencereVarMiD2)
            {
                
                d2PencereBaslangic = pencereBaslangicD2;
                d2PencereBitis = pencereBitisD2;
                duvar2dePencere = true;
            }

            return (d1PencereBaslangic, d1PencereBitis, duvar1dePencere, duvar2dePencere,
                    d2PencereBaslangic, d2PencereBitis);
        }

        
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

        
        public List<(int skor, List<(string, int)> dizilim, List<string> log)> Uret(int duvar1, int duvar2, List<(int basla, int bitir)> yasakliAlanlar = null, List<(string, int)> altDizilim = null)
        {
            var sonuc = new List<(int skor, List<(string, int)> dizilim, List<string> log)>();

            for (int i = 0; i < 1000000; i++) // Sabit 1 milyon dizilim
            {
                var layout = GenerateLDuzeni(duvar1, duvar2, yasakliAlanlar, altDizilim); // 4 parametre geç

                if (layout == null) continue;

                List<string> log;
                int skor = Evaluate(layout, duvar1 + duvar2, out log);
                sonuc.Add((skor, layout, log));

                // Her 50,000'de bir progress göster
                if (i % 50000 == 0 && i > 0)
                {
                    // Progress gösterimi kaldırıldı
                }
            }

            return sonuc.OrderByDescending(x => x.Item1).ToList();
        }

        

        public List<(string, int)> TekUstDolapDizilimi(int duvar1,int duvar2,List<(string, int)> altDizilim = null)
        {
            if (duvar2 == 0)            // düz duvar
            {
                var layout = GenerateUstDuzDuvar(duvar1, altDizilim);
                EnsureFullWidth(layout, duvar1);   // duvarı tamamen kapla
                return layout;
            }

            // L-duvar
            var lLayout = GenerateLDuzeni(duvar1, duvar2, null, altDizilim);
            EnsureFullWidth(lLayout, duvar1 + duvar2);
            return lLayout;
        }

        private void EnsureFullWidth(List<(string, int)> mods, int hedef)
        {
            int toplam = mods.Sum(m => m.Item2);
            if (toplam < hedef)
                mods.Add(("kalanAlanıDoldurma", hedef - toplam));
        }
        // ★ YENİ: Köşe dolap mantığıyla GenerateLDuzeni
        private List<(string, int)> GenerateLDuzeni(int duvar1, int duvar2, List<(int basla, int bitir)> yasakliAlanlar = null, List<(string, int)> altDizilim = null)
        {
            // Üst dolap köşe modülü seç
            var corner = CORNER_MODULES[rnd.Next(CORNER_MODULES.Count)];

            // YENİ: BUZDOLABI POZİSYON TESPİTİ (POZİSYON TEMELLİ SİSTEM)
            var buzdolabiPozisyon = HesaplaBuzdolabiPozisyonu(altDizilim, duvar1, duvar2, corner);

            // FIRIN POZİSYON TESPİTİ (MEVCUT SİSTEM)
            var firinPozisyon = HesaplaFireinPozisyonu(altDizilim, duvar1, duvar2, corner);

            // KİLER POZİSYON TESPİTİ (MEVCUT SİSTEM)
            var kilerPozisyon = HesaplaKilerPozisyonu(altDizilim, duvar1, duvar2, corner);

            // YATAY KISIM (DUVAR1) ÜRETİMİ
            int yatayHedef = duvar1 - corner.width1;
            var yatayModuller = new List<(string, int)>();

            // Pencere pozisyon hesaplama
            var pencerePozisyon = HesaplaPencerePozisyonu(duvar1, duvar2, corner);

            if (pencerePozisyon.duvar1de)
            {
                // DUVAR1'de pencere var
                yatayModuller = PencereliDuvar1Dizilimi(yatayHedef, pencerePozisyon, buzdolabiPozisyon, firinPozisyon, kilerPozisyon);
            }
            else
            {
                if (buzdolabiPozisyon.duvar1de && firinPozisyon.duvar1de && kilerPozisyon.duvar1de)
                {
                    // Buzdolabı + Fırın + Kiler DUVAR1'de - SÜPER KARMAŞIK
                    yatayModuller = SuperKarmasikDuvar1Dizilimi(yatayHedef, buzdolabiPozisyon, firinPozisyon, kilerPozisyon);
                }
                else if (buzdolabiPozisyon.duvar1de && firinPozisyon.duvar1de)
                {
                    // Buzdolabı + Fırın DUVAR1'de - KARMAŞIK
                    yatayModuller = KarmasikDuvar1Dizilimi(yatayHedef, buzdolabiPozisyon, firinPozisyon);
                }
                else if (buzdolabiPozisyon.duvar1de && kilerPozisyon.duvar1de)
                {
                    // Buzdolabı + Kiler DUVAR1'de
                    yatayModuller = BuzdolabiKilerDuvar1Dizilimi(yatayHedef, buzdolabiPozisyon, kilerPozisyon);
                }
                else if (firinPozisyon.duvar1de && kilerPozisyon.duvar1de)
                {
                    // Fırın + Kiler DUVAR1'de
                    yatayModuller = FireinKilerDuvar1Dizilimi(yatayHedef, firinPozisyon, kilerPozisyon);
                }
                else if (buzdolabiPozisyon.duvar1de)
                {
                    // Sadece buzdolabı DUVAR1'de (YENİ POZİSYON SİSTEMİ)
                    yatayModuller = BuzdolabiBoslukluDizilim(yatayHedef, buzdolabiPozisyon.buzdolabiBaslangic, buzdolabiPozisyon.buzdolabiBitis, true);
                }
                else if (firinPozisyon.duvar1de)
                {
                    // Sadece fırın DUVAR1'de
                    yatayModuller = FireinBoslukluDizilim(yatayHedef, firinPozisyon.firinBaslangic, firinPozisyon.firinBitis, true);
                }
                else if (kilerPozisyon.duvar1de)
                {
                    // Sadece kiler DUVAR1'de
                    yatayModuller = KilerBoslukluDizilim(yatayHedef, kilerPozisyon.kilerBaslangic, kilerPozisyon.kilerBitis, true);
                }
                else
                {
                    // Normal dizilim
                    yatayModuller = NormalUstDolapDizilimi(yatayHedef);
                }
            }

            // DİKEY KISIM (DUVAR2) ÜRETİMİ  
            int dikeyHedef = duvar2 - corner.width2;
            var dikeyModuller = new List<(string, int)>();

            if (pencerePozisyon.duvar2de)
            {
                // DUVAR2'de pencere var
                dikeyModuller = PencereliDuvar2Dizilimi(dikeyHedef, pencerePozisyon, buzdolabiPozisyon, firinPozisyon, kilerPozisyon, duvar1);
            }
            else if (buzdolabiPozisyon.duvar2de && firinPozisyon.duvar2de && kilerPozisyon.duvar2de)
            {
                // Buzdolabı + Fırın + Kiler DUVAR2'de - SÜPER KARMAŞIK
                dikeyModuller = SuperKarmasikDuvar2Dizilimi(dikeyHedef, buzdolabiPozisyon, firinPozisyon, kilerPozisyon, duvar1);
            }
            else if (buzdolabiPozisyon.duvar2de && firinPozisyon.duvar2de)
            {
                // Buzdolabı + Fırın DUVAR2'de
                dikeyModuller = KarmasikDuvar2Dizilimi(dikeyHedef, buzdolabiPozisyon, firinPozisyon, duvar1);
            }
            else if (buzdolabiPozisyon.duvar2de && kilerPozisyon.duvar2de)
            {
                // Buzdolabı + Kiler DUVAR2'de
                dikeyModuller = BuzdolabiKilerDuvar2Dizilimi(dikeyHedef, buzdolabiPozisyon, kilerPozisyon, duvar1);
            }
            else if (firinPozisyon.duvar2de && kilerPozisyon.duvar2de)
            {
                // Fırın + Kiler DUVAR2'de
                dikeyModuller = FireinKilerDuvar2Dizilimi(dikeyHedef, firinPozisyon, kilerPozisyon, duvar1);
            }
            else if (buzdolabiPozisyon.duvar2de)
            {
                // Sadece buzdolabı DUVAR2'de (YENİ POZİSYON SİSTEMİ)
                dikeyModuller = BuzdolabiBoslukluDizilim(dikeyHedef, buzdolabiPozisyon.buzdolabiBaslangic - duvar1, buzdolabiPozisyon.buzdolabiBitis - duvar1, false);
            }
            else if (firinPozisyon.duvar2de)
            {
                // Sadece fırın DUVAR2'de
                dikeyModuller = FireinBoslukluDizilim(dikeyHedef, firinPozisyon.firinBaslangic - duvar1, firinPozisyon.firinBitis - duvar1, false);
            }
            else if (kilerPozisyon.duvar2de)
            {
                // Sadece kiler DUVAR2'de
                dikeyModuller = KilerBoslukluDizilim(dikeyHedef, kilerPozisyon.kilerBaslangic - duvar1, kilerPozisyon.kilerBitis - duvar1, false);
            }
            else
            {
                // Normal dizilim
                dikeyModuller = NormalUstDolapDizilimi(dikeyHedef);
            }

            // BİRLEŞTİRME
            var result = new List<(string, int)>();
            result.AddRange(yatayModuller);
            result.Add((corner.type + "_1", corner.width1));
            result.Add((corner.type + "_2", corner.width2));
            result.AddRange(dikeyModuller);

            return result;
        }



        private List<(string, int)> PencereliDuvar1Dizilimi(int hedefGenislik,
   (int pencereBaslangic, int pencereBitis, bool duvar1de, bool duvar2de,
    int pencereBaslangicD2, int pencereBitisD2) pencerePozisyon,
   (int buzdolabiBaslangic, int buzdolabiBitis, bool duvar1de, bool duvar2de) buzdolabiPozisyon,
   (int firinBaslangic, int firinBitis, bool duvar1de, bool duvar2de) firinPozisyon,
   (int kilerBaslangic, int kilerBitis, bool duvar1de, bool duvar2de) kilerPozisyon)
        {
            var moduller = new List<(string, int)>();
            int pencereGenislik = pencerePozisyon.pencereBitis - pencerePozisyon.pencereBaslangic;

            // Tüm boşlukları (buzdolabı, fırın, kiler, pencere) pozisyona göre sırala
            var bosluklarListesi = new List<(int baslangic, int bitis, string tip)>();

            if (buzdolabiPozisyon.duvar1de)
                bosluklarListesi.Add((buzdolabiPozisyon.buzdolabiBaslangic, buzdolabiPozisyon.buzdolabiBitis, "bosluk"));

            if (firinPozisyon.duvar1de)
                bosluklarListesi.Add((firinPozisyon.firinBaslangic, firinPozisyon.firinBitis, "bosluk2"));

            if (kilerPozisyon.duvar1de)
                bosluklarListesi.Add((kilerPozisyon.kilerBaslangic, kilerPozisyon.kilerBitis, "bosluk1"));

            // Pencereyi ekle
            bosluklarListesi.Add((pencerePozisyon.pencereBaslangic, pencerePozisyon.pencereBitis, "pencere"));

            // Pozisyona göre sırala
            bosluklarListesi = bosluklarListesi.OrderBy(x => x.baslangic).ToList();

            // Sıralı boşluklar arasına dolap yerleştir
            int currentPos = 0;
            foreach (var bosluk in bosluklarListesi)
            {
                // Boşluğa kadar dolap
                if (bosluk.baslangic > currentPos)
                {
                    int dolapAlani = bosluk.baslangic - currentPos;
                    var dolaplar = NormalUstDolapDizilimi(dolapAlani);
                    moduller.AddRange(dolaplar);
                }

                // Boşluğu yerleştir
                int boslukGenislik = bosluk.bitis - bosluk.baslangic;
                moduller.Add((bosluk.tip, boslukGenislik));
                currentPos = bosluk.bitis;
            }

            // Son kalan alan
            if (currentPos < hedefGenislik)
            {
                int kalanAlan = hedefGenislik - currentPos;
                var sonDolaplar = NormalUstDolapDizilimi(kalanAlan);
                moduller.AddRange(sonDolaplar);
            }

            return moduller;
        }

        // SuperKarmasikDuvar1Dizilimi methodu güncellemesi
        private List<(string, int)> SuperKarmasikDuvar1Dizilimi(int hedefGenislik,
            (int buzdolabiBaslangic, int buzdolabiBitis, bool duvar1de, bool duvar2de) buzdolabiPozisyon,
            (int firinBaslangic, int firinBitis, bool duvar1de, bool duvar2de) firinPozisyon,
            (int kilerBaslangic, int kilerBitis, bool duvar1de, bool duvar2de) kilerPozisyon)
        {
            var moduller = new List<(string, int)>();

            // Pozisyonları sırala
            var pozisyonlar = new List<(int baslangic, int bitis, string tip)>
    {
        (buzdolabiPozisyon.buzdolabiBaslangic, buzdolabiPozisyon.buzdolabiBitis, "bosluk"),
        (firinPozisyon.firinBaslangic, firinPozisyon.firinBitis, "bosluk2"),
        (kilerPozisyon.kilerBaslangic, kilerPozisyon.kilerBitis, "bosluk1")
    }.OrderBy(x => x.baslangic).ToList();

            int currentPos = 0;

            foreach (var poz in pozisyonlar)
            {
                // Pozisyona kadar dolap
                if (poz.baslangic > currentPos)
                {
                    int araAlan = poz.baslangic - currentPos;
                    var araDolaplar = NormalUstDolapDizilimi(araAlan);
                    moduller.AddRange(araDolaplar);
                }

                // Boşluğu ekle
                int genislik = poz.bitis - poz.baslangic;
                moduller.Add((poz.tip, genislik));
                currentPos = poz.bitis;
            }

            // Son kalan alan
            if (currentPos < hedefGenislik)
            {
                int kalanAlan = hedefGenislik - currentPos;
                var sonDolaplar = NormalUstDolapDizilimi(kalanAlan);
                moduller.AddRange(sonDolaplar);
            }

            return moduller;
        }

        // SuperKarmasikDuvar2Dizilimi methodu güncellemesi
        private List<(string, int)> SuperKarmasikDuvar2Dizilimi(int hedefGenislik,
            (int buzdolabiBaslangic, int buzdolabiBitis, bool duvar1de, bool duvar2de) buzdolabiPozisyon,
            (int firinBaslangic, int firinBitis, bool duvar1de, bool duvar2de) firinPozisyon,
            (int kilerBaslangic, int kilerBitis, bool duvar1de, bool duvar2de) kilerPozisyon, int duvar1)
        {
            var moduller = new List<(string, int)>();

            // DUVAR2'deki pozisyonları hesapla
            var duvar2Pozisyonlar = new List<(int baslangic, int bitis, string tip)>
    {
        (buzdolabiPozisyon.buzdolabiBaslangic - duvar1, buzdolabiPozisyon.buzdolabiBitis - duvar1, "bosluk"),
        (firinPozisyon.firinBaslangic - duvar1, firinPozisyon.firinBitis - duvar1, "bosluk2"),
        (kilerPozisyon.kilerBaslangic - duvar1, kilerPozisyon.kilerBitis - duvar1, "bosluk1")
    }.OrderBy(x => x.baslangic).ToList();

            int currentPos = 0;

            foreach (var poz in duvar2Pozisyonlar)
            {
                // Pozisyona kadar dolap
                if (poz.baslangic > currentPos)
                {
                    int araAlan = poz.baslangic - currentPos;
                    var araDolaplar = NormalUstDolapDizilimi(araAlan);
                    moduller.AddRange(araDolaplar);
                    currentPos += araAlan;
                }

                // Boşluğu ekle
                int genislik = poz.bitis - poz.baslangic;
                moduller.Add((poz.tip, genislik));
                currentPos = poz.bitis;
            }

            // Son kalan alan
            if (currentPos < hedefGenislik)
            {
                int kalanAlan = hedefGenislik - currentPos;
                var sonDolaplar = NormalUstDolapDizilimi(kalanAlan);
                moduller.AddRange(sonDolaplar);
            }

            return moduller;
        }

        // BuzdolabiKilerDuvar1Dizilimi methodu güncellemesi
        private List<(string, int)> BuzdolabiKilerDuvar1Dizilimi(int hedefGenislik,
            (int buzdolabiBaslangic, int buzdolabiBitis, bool duvar1de, bool duvar2de) buzdolabiPozisyon,
            (int kilerBaslangic, int kilerBitis, bool duvar1de, bool duvar2de) kilerPozisyon)
        {
            var moduller = new List<(string, int)>();

            // Pozisyonları sırala
            var pozisyonlar = new List<(int baslangic, int bitis, string tip)>
    {
        (buzdolabiPozisyon.buzdolabiBaslangic, buzdolabiPozisyon.buzdolabiBitis, "bosluk"),
        (kilerPozisyon.kilerBaslangic, kilerPozisyon.kilerBitis, "bosluk1")
    }.OrderBy(x => x.baslangic).ToList();

            int currentPos = 0;

            foreach (var poz in pozisyonlar)
            {
                // Pozisyona kadar dolap
                if (poz.baslangic > currentPos)
                {
                    int araAlan = poz.baslangic - currentPos;
                    var araDolaplar = NormalUstDolapDizilimi(araAlan);
                    moduller.AddRange(araDolaplar);
                }

                // Boşluğu ekle
                int genislik = poz.bitis - poz.baslangic;
                moduller.Add((poz.tip, genislik));
                currentPos = poz.bitis;
            }

            // Son kalan alan
            if (currentPos < hedefGenislik)
            {
                int kalanAlan = hedefGenislik - currentPos;
                var sonDolaplar = NormalUstDolapDizilimi(kalanAlan);
                moduller.AddRange(sonDolaplar);
            }

            return moduller;
        }

        // BuzdolabiKilerDuvar2Dizilimi methodu güncellemesi  
        private List<(string, int)> BuzdolabiKilerDuvar2Dizilimi(int dikeyHedef,
            (int buzdolabiBaslangic, int buzdolabiBitis, bool duvar1de, bool duvar2de) buzdolabiPozisyon,
            (int kilerBaslangic, int kilerBitis, bool duvar1de, bool duvar2de) kilerPozisyon,
            int duvar1)
        {
            var moduller = new List<(string, int)>();

            // DUVAR2'deki pozisyonları hesapla
            var duvar2Pozisyonlar = new List<(int baslangic, int bitis, string tip)>
    {
        (buzdolabiPozisyon.buzdolabiBaslangic - duvar1, buzdolabiPozisyon.buzdolabiBitis - duvar1, "bosluk"),
        (kilerPozisyon.kilerBaslangic - duvar1, kilerPozisyon.kilerBitis - duvar1, "bosluk1")
    }.OrderBy(x => x.baslangic).ToList();

            int currentPos = 0;

            foreach (var poz in duvar2Pozisyonlar)
            {
                // Pozisyona kadar dolap
                if (poz.baslangic > currentPos)
                {
                    int araAlan = poz.baslangic - currentPos;
                    var araDolaplar = NormalUstDolapDizilimi(araAlan);
                    moduller.AddRange(araDolaplar);
                    currentPos += araAlan;
                }

                // Boşluğu ekle
                int genislik = poz.bitis - poz.baslangic;
                moduller.Add((poz.tip, genislik));
                currentPos = poz.bitis;
            }

            // Son kalan alan
            if (currentPos < dikeyHedef)
            {
                int kalanAlan = dikeyHedef - currentPos;
                var sonDolaplar = NormalUstDolapDizilimi(kalanAlan);
                moduller.AddRange(sonDolaplar);
            }

            return moduller;
        }

        // KarmasikDuvar1Dizilimi methodu güncellemesi
        private List<(string, int)> KarmasikDuvar1Dizilimi(int hedefGenislik,
            (int buzdolabiBaslangic, int buzdolabiBitis, bool duvar1de, bool duvar2de) buzdolabiPozisyon,
            (int firinBaslangic, int firinBitis, bool duvar1de, bool duvar2de) firinPozisyon)
        {
            var moduller = new List<(string, int)>();

            // Pozisyonları sırala
            var pozisyonlar = new List<(int baslangic, int bitis, string tip)>
    {
        (buzdolabiPozisyon.buzdolabiBaslangic, buzdolabiPozisyon.buzdolabiBitis, "bosluk"),
        (firinPozisyon.firinBaslangic, firinPozisyon.firinBitis, "bosluk2")
    }.OrderBy(x => x.baslangic).ToList();

            int currentPos = 0;

            foreach (var poz in pozisyonlar)
            {
                // Pozisyona kadar dolap
                if (poz.baslangic > currentPos)
                {
                    int araAlan = poz.baslangic - currentPos;
                    var araDolaplar = NormalUstDolapDizilimi(araAlan);
                    moduller.AddRange(araDolaplar);
                }

                // Boşluğu ekle
                int genislik = poz.bitis - poz.baslangic;
                moduller.Add((poz.tip, genislik));
                currentPos = poz.bitis;
            }

            // Son kalan alan
            if (currentPos < hedefGenislik)
            {
                int kalanAlan = hedefGenislik - currentPos;
                var sonDolaplar = NormalUstDolapDizilimi(kalanAlan);
                moduller.AddRange(sonDolaplar);
            }

            return moduller;
        }

        // KarmasikDuvar2Dizilimi methodu güncellemesi
        private List<(string, int)> KarmasikDuvar2Dizilimi(int hedefGenislik,
            (int buzdolabiBaslangic, int buzdolabiBitis, bool duvar1de, bool duvar2de) buzdolabiPozisyon,
            (int firinBaslangic, int firinBitis, bool duvar1de, bool duvar2de) firinPozisyon,
            int duvar1)
        {
            var moduller = new List<(string, int)>();

            // DUVAR2'deki pozisyonları hesapla
            var duvar2Pozisyonlar = new List<(int baslangic, int bitis, string tip)>
    {
        (buzdolabiPozisyon.buzdolabiBaslangic - duvar1, buzdolabiPozisyon.buzdolabiBitis - duvar1, "bosluk"),
        (firinPozisyon.firinBaslangic - duvar1, firinPozisyon.firinBitis - duvar1, "bosluk2")
    }.OrderBy(x => x.baslangic).ToList();

            int currentPos = 0;

            foreach (var poz in duvar2Pozisyonlar)
            {
                // Pozisyona kadar dolap
                if (poz.baslangic > currentPos)
                {
                    int araAlan = poz.baslangic - currentPos;
                    var araDolaplar = NormalUstDolapDizilimi(araAlan);
                    moduller.AddRange(araDolaplar);
                    currentPos += araAlan;
                }

                // Boşluğu ekle
                int genislik = poz.bitis - poz.baslangic;
                moduller.Add((poz.tip, genislik));
                currentPos = poz.bitis;
            }

            // Son kalan alan
            if (currentPos < hedefGenislik)
            {
                int kalanAlan = hedefGenislik - currentPos;
                var sonDolaplar = NormalUstDolapDizilimi(kalanAlan);
                moduller.AddRange(sonDolaplar);
            }

            return moduller;
        }

        // PencereliDuvar2Dizilimi methodu güncellemesi
        private List<(string, int)> PencereliDuvar2Dizilimi(int dikeyHedef,
            (int pencereBaslangic, int pencereBitis, bool duvar1de, bool duvar2de,
             int pencereBaslangicD2, int pencereBitisD2) pencerePozisyon,
            (int buzdolabiBaslangic, int buzdolabiBitis, bool duvar1de, bool duvar2de) buzdolabiPozisyon,
            (int firinBaslangic, int firinBitis, bool duvar1de, bool duvar2de) firinPozisyon,
            (int kilerBaslangic, int kilerBitis, bool duvar1de, bool duvar2de) kilerPozisyon,
            int duvar1)
        {
            var dikeyModuller = new List<(string, int)>();

            int pencereBaslangic = pencerePozisyon.pencereBaslangicD2;
            int pencereBitis = pencerePozisyon.pencereBitisD2;
            int pencereGenislik = pencereBitis - pencereBaslangic;

            var bosluklarListesi = new List<(int baslangic, int bitis, string tip)>();

            bosluklarListesi.Add((pencereBaslangic, pencereBitis, "pencere"));

            if (buzdolabiPozisyon.duvar2de)
            {
                int buzdolabiD2Baslangic = buzdolabiPozisyon.buzdolabiBaslangic - duvar1;
                int buzdolabiD2Bitis = buzdolabiPozisyon.buzdolabiBitis - duvar1;
                bosluklarListesi.Add((buzdolabiD2Baslangic, buzdolabiD2Bitis, "bosluk"));
            }

            if (firinPozisyon.duvar2de)
            {
                int firinD2Baslangic = firinPozisyon.firinBaslangic - duvar1;
                int firinD2Bitis = firinPozisyon.firinBitis - duvar1;
                bosluklarListesi.Add((firinD2Baslangic, firinD2Bitis, "bosluk2"));
            }

            if (kilerPozisyon.duvar2de)
            {
                int kilerD2Baslangic = kilerPozisyon.kilerBaslangic - duvar1;
                int kilerD2Bitis = kilerPozisyon.kilerBitis - duvar1;
                bosluklarListesi.Add((kilerD2Baslangic, kilerD2Bitis, "bosluk1"));
            }

            bosluklarListesi = bosluklarListesi.OrderBy(x => x.baslangic).ToList();

            int currentPos = 0;
            foreach (var bosluk in bosluklarListesi)
            {
                if (bosluk.baslangic > currentPos)
                {
                    int dolapAlani = bosluk.baslangic - currentPos;
                    var dolaplar = NormalUstDolapDizilimi(dolapAlani);
                    dikeyModuller.AddRange(dolaplar);
                }

                int boslukGenislik = bosluk.bitis - bosluk.baslangic;
                dikeyModuller.Add((bosluk.tip, boslukGenislik));
                currentPos = bosluk.bitis;
            }

            if (currentPos < dikeyHedef)
            {
                int kalanAlan = dikeyHedef - currentPos;
                var sonDolaplar = NormalUstDolapDizilimi(kalanAlan);
                dikeyModuller.AddRange(sonDolaplar);
            }

            return dikeyModuller;
        }


        private List<(string, int)> KilerBoslukluDizilim(int hedefGenislik, int kilerBaslangic, int kilerBitis, bool duvar1de = false)
        {
            var moduller = new List<(string, int)>();
            int kilerGenislik = kilerBitis - kilerBaslangic;

            // ✅ Artık pozisyon düzeltmesi yapmaya gerek yok
            // HesaplaKilerPozisyonu zaten doğru pozisyonu döndürüyor

            // Kiler başlangıcına kadar dolap
            if (kilerBaslangic > 0)
            {
                var oncesiDolaplar = NormalUstDolapDizilimi(kilerBaslangic);
                moduller.AddRange(oncesiDolaplar);
            }

            // Kiler boşluğu
            moduller.Add(("bosluk1", kilerGenislik));

            // Kiler sonrası kalan alan
            int kalanAlan = hedefGenislik - kilerBitis;
            if (kalanAlan > 0)
            {
                var sonrasiDolaplar = NormalUstDolapDizilimi(kalanAlan);
                moduller.AddRange(sonrasiDolaplar);
            }

            return moduller;
        }




        private List<(string, int)> FireinKilerDuvar1Dizilimi(int hedefGenislik,
    (int firinBaslangic, int firinBitis, bool duvar1de, bool duvar2de) firinPozisyon,
    (int kilerBaslangic, int kilerBitis, bool duvar1de, bool duvar2de) kilerPozisyon)
        {
            var moduller = new List<(string, int)>();
            int firinGenislik = firinPozisyon.firinBitis - firinPozisyon.firinBaslangic;
            int kilerGenislik = kilerPozisyon.kilerBitis - kilerPozisyon.kilerBaslangic;

            // Pozisyonları sırala (hangisi önce geliyor)
            var pozisyonlar = new List<(int baslangic, int bitis, string tip, int genislik)>
    {
        (firinPozisyon.firinBaslangic, firinPozisyon.firinBitis, "bosluk2", firinGenislik),
        (kilerPozisyon.kilerBaslangic, kilerPozisyon.kilerBitis, "bosluk1", kilerGenislik)
    }.OrderBy(x => x.baslangic).ToList();

            int currentPos = 0;

            foreach (var poz in pozisyonlar)
            {
                // Pozisyona kadar dolap
                if (poz.baslangic > currentPos)
                {
                    int araAlan = poz.baslangic - currentPos;
                    var araDolaplar = NormalUstDolapDizilimi(araAlan);
                    moduller.AddRange(araDolaplar);
                }

                // Boşluğu ekle
                moduller.Add((poz.tip, poz.genislik));
                currentPos = poz.bitis;
            }

            // Son kalan alan
            if (currentPos < hedefGenislik)
            {
                int kalanAlan = hedefGenislik - currentPos;
                var sonDolaplar = NormalUstDolapDizilimi(kalanAlan);
                moduller.AddRange(sonDolaplar);
            }

            return moduller;
        }


  

        private List<(string, int)> FireinKilerDuvar2Dizilimi(int hedefGenislik,
    (int firinBaslangic, int firinBitis, bool duvar1de, bool duvar2de) firinPozisyon,
    (int kilerBaslangic, int kilerBitis, bool duvar1de, bool duvar2de) kilerPozisyon, int duvar1)
        {
            var moduller = new List<(string, int)>();
            int firinGenislik = firinPozisyon.firinBitis - firinPozisyon.firinBaslangic;
            int kilerGenislik = kilerPozisyon.kilerBitis - kilerPozisyon.kilerBaslangic;

            
            var duvar2Pozisyonlar = new List<(int baslangic, int bitis, string tip, int genislik)>
    {
        (firinPozisyon.firinBaslangic - duvar1, firinPozisyon.firinBitis - duvar1, "bosluk2", firinGenislik),
        (kilerPozisyon.kilerBaslangic - duvar1, kilerPozisyon.kilerBitis - duvar1, "bosluk1", kilerGenislik)
    }.OrderBy(x => x.baslangic).ToList();

            int currentPos = 0;

            foreach (var poz in duvar2Pozisyonlar)
            {
                // Pozisyona kadar dolap
                if (poz.baslangic > currentPos)
                {
                    int araAlan = poz.baslangic - currentPos;
                    var araDolaplar = NormalUstDolapDizilimi(araAlan);
                    moduller.AddRange(araDolaplar);
                    currentPos += araAlan;
                }

                // Boşluğu ekle
                moduller.Add((poz.tip, poz.genislik));
                currentPos += poz.genislik; 
            }

            // Son kalan alan
            if (currentPos < hedefGenislik)
            {
                int kalanAlan = hedefGenislik - currentPos;
                var sonDolaplar = NormalUstDolapDizilimi(kalanAlan);
                moduller.AddRange(sonDolaplar);
            }

            return moduller;
        }


        



        private List<(string, int)> NormalUstDolapDizilimi(int hedefGenislik)
        {
            var moduller = new List<(string, int)>();
            int mevcutGenislik = 0;

            // 1️⃣ Önce optimal kombinasyon dene (Dynamic Programming yaklaşımı)
            var optimalKombinasyon = BulOptimalKombinasyon(hedefGenislik);

            if (optimalKombinasyon != null && optimalKombinasyon.Sum() == hedefGenislik)
            {
                // Optimal kombinasyon bulundu - tam dolduruyor!
                foreach (int boyut in optimalKombinasyon)
                {
                    moduller.Add(("dolap", boyut));
                }
                return moduller;
            }

            // 2️⃣ Optimal bulunamadıysa, akıllı greedy algoritması
            while (mevcutGenislik < hedefGenislik)
            {
                int kalanAlan = hedefGenislik - mevcutGenislik;

                // Tam oturan boyut var mı?
                var tamOturan = MODULES["dolap"].FirstOrDefault(w => w == kalanAlan);
                if (tamOturan > 0)
                {
                    moduller.Add(("dolap", tamOturan));
                    mevcutGenislik += tamOturan;
                    break; // Tam dolduruldu!
                }

                // Küçük kalan alan için optimal arama
                if (kalanAlan <= 200) // 2 metreye kadar optimal kombinasyon ara
                {
                    var kucukOptimal = BulOptimalKombinasyon(kalanAlan);
                    if (kucukOptimal != null)
                    {
                        foreach (int boyut in kucukOptimal)
                        {
                            moduller.Add(("dolap", boyut));
                            mevcutGenislik += boyut;
                        }
                        break;
                    }
                }

                // Hâlâ bulunamadıysa, en büyük uygun modülü seç (eski yöntem)
                var uygunModuller = MODULES["dolap"].Where(w => w <= kalanAlan).OrderByDescending(w => w).ToList();

                if (uygunModuller.Any())
                {
                    int secilenModul = uygunModuller.First();
                    moduller.Add(("dolap", secilenModul));
                    mevcutGenislik += secilenModul;
                }
                else
                {
                    // Son çare: kalan alanı doldur
                    if (kalanAlan >= 5)
                    {
                        moduller.Add(("kalanAlanıDoldurma", kalanAlan));
                        mevcutGenislik += kalanAlan;
                    }
                    break;
                }
            }

            return moduller;
        }


        private List<int> BulOptimalKombinasyon(int hedef)
        {
            var dolapBoyutlari = MODULES["dolap"]; 

           
            if (hedef <= 300)
            {
                return BulTamKombinasyon(dolapBoyutlari.ToList(), hedef, new List<int>());
            }

            
            return BulEnIyiKombinasyon(dolapBoyutlari.ToList(), hedef);
        }

       
        private List<int> BulTamKombinasyon(List<int> boyutlar, int hedef, List<int> mevcutKombinasyon)
        {
            if (hedef == 0) return new List<int>(mevcutKombinasyon); // Tam bulundu!
            if (hedef < 0 || boyutlar.Count == 0) return null;

            // Her boyutu dene
            for (int i = 0; i < boyutlar.Count; i++)
            {
                int boyut = boyutlar[i];
                if (boyut <= hedef)
                {
                    // Bu boyutu ekleyip recursive ara
                    mevcutKombinasyon.Add(boyut);
                    var sonuc = BulTamKombinasyon(boyutlar, hedef - boyut, mevcutKombinasyon);
                    if (sonuc != null) return sonuc;
                    mevcutKombinasyon.RemoveAt(mevcutKombinasyon.Count - 1); // Backtrack
                }
            }

            return null; // Tam kombinasyon bulunamadı
        }

        
        private List<int> BulEnIyiKombinasyon(List<int> boyutlar, int hedef)
        {
            var sonuc = new List<int>();
            int kalan = hedef;
            var sirali = boyutlar.OrderByDescending(x => x).ToList();

            // Büyükten küçüğe optimal dağıtım
            while (kalan > 0)
            {
                bool bulundu = false;

                // Tam oturan var mı?
                var tamOturan = sirali.FirstOrDefault(x => x == kalan);
                if (tamOturan > 0)
                {
                    sonuc.Add(tamOturan);
                    break; // Tamamen dolduruldu
                }

                // En büyük uygun modülü seç
                var uygun = sirali.FirstOrDefault(x => x <= kalan);
                if (uygun > 0)
                {
                    sonuc.Add(uygun);
                    kalan -= uygun;
                    bulundu = true;
                }

                if (!bulundu) break; // Daha fazla eklenemez
            }

            return kalan == 0 ? sonuc : null; // Sadece tam doldurursa döndür
        }

       
        private (int firinBaslangic, int firinBitis, bool duvar1de, bool duvar2de) HesaplaFireinPozisyonu(
    List<(string, int)> altDizilim, int duvar1, int duvar2, (string type, int width1, int width2) ustKose)
        {
            if (altDizilim == null) return (0, 0, false, false);

            int totalPos = 0;
            bool koseGecildi = false;
            int altKoseWidth2 = 0; // Alt köşenin DUVAR2 genişliği

            foreach (var modul in altDizilim)
            {
                if (modul.Item1.Contains("kose"))
                {
                    if (modul.Item1.Contains("_1") && !koseGecildi)
                    {
                        koseGecildi = true;
                        totalPos += modul.Item2; // DUVAR1 boyutu eklendi
                    }
                    else if (modul.Item1.Contains("_2") && koseGecildi)
                    {
                        altKoseWidth2 = modul.Item2; // Alt köşenin DUVAR2 genişliğini kaydet
                                                     // DUVAR2'de pozisyon hesabı köşe genişliği olmadan devam eder
                    }
                    continue;
                }

                // FIRIN BULUNDU!
                if (modul.Item1.Contains("firin") || modul.Item1.Contains("aspirator") || modul.Item1.Contains("CEK"))
                {
                    if (koseGecildi && altKoseWidth2 > 0)
                    {
                        // DUVAR2'de - köşe farkı düzeltmesi
                        // Alt köşe genişliği - Üst köşe genişliği = fark
                        int koseFarki = altKoseWidth2 - ustKose.width2;

                        // Pozisyonu düzelt (köşe farkı kadar geri çek)
                        int duzeltilmisBaslangic = totalPos + koseFarki;
                        int duzeltilmisBitis = duzeltilmisBaslangic + modul.Item2;

                        return (duzeltilmisBaslangic, duzeltilmisBitis, false, true);
                    }
                    else
                    {
                        // DUVAR1'de - düzeltme gerek yok
                        return (totalPos, totalPos + modul.Item2, true, false);
                    }
                }

                totalPos += modul.Item2;
            }

            return (0, 0, false, false);
        }

        
        private List<(string, int)> FireinBoslukluDizilim(int hedefGenislik, int firinBaslangic, int firinBitis, bool duvar1de = false)
        {
            var moduller = new List<(string, int)>();
            int firinGenislik = firinBitis - firinBaslangic;

            // ✅ Artık pozisyon düzeltmesi yapmaya gerek yok
            // HesaplaFireinPozisyonu zaten doğru pozisyonu döndürüyor

            // Fırın başlangıcına kadar dolap
            if (firinBaslangic > 0)
            {
                var oncesiDolaplar = NormalUstDolapDizilimi(firinBaslangic);
                moduller.AddRange(oncesiDolaplar);
            }

            // Fırın boşluğu
            moduller.Add(("bosluk2", firinGenislik));

            // Fırın sonrası kalan alan
            int kalanAlan = hedefGenislik - firinBitis;
            if (kalanAlan > 0)
            {
                var sonrasiDolaplar = NormalUstDolapDizilimi(kalanAlan);
                moduller.AddRange(sonrasiDolaplar);
            }

            return moduller;
        }

        private List<(string, int)> GenerateUstDuzDuvar(int duvarUzunlugu, List<(string, int)> altDizilim = null)
        {
            // Tek duvar: köşe yok
            var altAnaliz = AnalizEtAltDizilim(altDizilim, duvarUzunlugu);

            // Var olan metotların beklediği pozisyon tiplerine çevir
            var firinPoz = altAnaliz.firinVar
                ? (altAnaliz.firinBaslangic, altAnaliz.firinBaslangic + altAnaliz.firinGenislik, true, false)
                : (0, 0, false, false);

            var kilerPoz = altAnaliz.kilerVar
                ? (altAnaliz.kilerBaslangic, altAnaliz.kilerBaslangic + altAnaliz.kilerGenislik, true, false)
                : (0, 0, false, false);

            // YENİ: Buzdolabı pozisyon bilgisi
            var buzdolabiPoz = altAnaliz.buzdolabiVar
                ? (altAnaliz.buzdolabiBaslangic, altAnaliz.buzdolabiBaslangic + altAnaliz.buzdolabiGenislik, true, false)
                : (0, 0, false, false);

            // Pencere: mevcut alanlardan oku
            if (pencereVarMi && pencereBaslangic < duvarUzunlugu)
            {
                var pencerePoz = (pencereBaslangic, pencereBitis, true, false, 0, 0);
                return PencereliDuvar1Dizilimi(
                    duvarUzunlugu,
                    pencerePoz,
                    buzdolabiPoz, firinPoz, kilerPoz
                );
            }

            // Kombinasyonlar
            if (altAnaliz.buzdolabiVar && altAnaliz.firinVar && altAnaliz.kilerVar)
            {
                // Buzdolabı + Fırın + Kiler
                return SuperKarmasikDuvar1Dizilimi(duvarUzunlugu, buzdolabiPoz, firinPoz, kilerPoz);
            }
            else if (altAnaliz.buzdolabiVar && altAnaliz.firinVar)
            {
                // Buzdolabı + Fırın
                return KarmasikDuvar1Dizilimi(duvarUzunlugu, buzdolabiPoz, firinPoz);
            }
            else if (altAnaliz.buzdolabiVar && altAnaliz.kilerVar)
            {
                // Buzdolabı + Kiler
                return BuzdolabiKilerDuvar1Dizilimi(duvarUzunlugu, buzdolabiPoz, kilerPoz);
            }
            else if (altAnaliz.firinVar && altAnaliz.kilerVar)
            {
                // Fırın + Kiler
                return FireinKilerDuvar1Dizilimi(duvarUzunlugu, firinPoz, kilerPoz);
            }
            else if (altAnaliz.buzdolabiVar)
            {
                // YENİ: Sadece buzdolabı boşluğu (pozisyon temelli)
                return BuzdolabiBoslukluDizilim(duvarUzunlugu, altAnaliz.buzdolabiBaslangic, altAnaliz.buzdolabiBaslangic + altAnaliz.buzdolabiGenislik, true);
            }
            else if (altAnaliz.firinVar)
            {
                // Sadece fırın/aspiratör boşluğu
                return FireinBoslukluDizilim(duvarUzunlugu, altAnaliz.firinBaslangic, altAnaliz.firinBaslangic + altAnaliz.firinGenislik, true);
            }
            else if (altAnaliz.kilerVar)
            {
                // Sadece kiler boşluğu
                return KilerBoslukluDizilim(duvarUzunlugu, altAnaliz.kilerBaslangic, altAnaliz.kilerBaslangic + altAnaliz.kilerGenislik, true);
            }

            // Hiçbiri yoksa: normal
            return NormalUstDolapDizilimi(duvarUzunlugu);
        }



        public class AltDizilimAnaliz
        {
            public bool buzdolabiVar { get; set; }
            public int buzdolabiBaslangic { get; set; }  // YENİ EKLENEN
            public int buzdolabiGenislik { get; set; }

            public bool firinVar { get; set; }
            public int firinBaslangic { get; set; }
            public int firinGenislik { get; set; }

            public bool kilerVar { get; set; }
            public int kilerBaslangic { get; set; }
            public int kilerGenislik { get; set; }
        }

        // AnalizEtAltDizilim methodu güncellemesi (buzdolabı pozisyon tespiti eklendi)
        private AltDizilimAnaliz AnalizEtAltDizilim(List<(string, int)> altDizilim, int duvarUzunlugu)
        {
            var analiz = new AltDizilimAnaliz();

            if (altDizilim == null) return analiz;

            int pozisyon = 0;
            foreach (var (tip, genislik) in altDizilim)
            {
                // Duvar sınırını aşan modülleri yoksay
                if (pozisyon >= duvarUzunlugu) break;

                // Sadece duvar içindeki kısmı analiz et
                int gercekGenislik = Math.Min(genislik, duvarUzunlugu - pozisyon);

                if (tip.Contains("buzdolabi") && !analiz.buzdolabiVar)
                {
                    analiz.buzdolabiVar = true;
                    analiz.buzdolabiBaslangic = pozisyon;  // YENİ EKLENEN
                    analiz.buzdolabiGenislik = gercekGenislik;
                }
                else if ((tip.Contains("firin") || tip.Contains("ocak")) && !analiz.firinVar)
                {
                    analiz.firinVar = true;
                    analiz.firinBaslangic = pozisyon;
                    analiz.firinGenislik = gercekGenislik;
                }
                else if (tip.Contains("kiler") && !analiz.kilerVar)
                {
                    analiz.kilerVar = true;
                    analiz.kilerBaslangic = pozisyon;
                    analiz.kilerGenislik = gercekGenislik;
                }

                pozisyon += genislik; // Orijinal genişliği ekle (pozisyon takibi için)
            }

            return analiz;
        }

        private List<(string, int)> GenerateUstV2Temiz(int duvar1, bool duzModu, List<(string, int)> altDizilim = null)
        {
            if (!duzModu)
            {
                throw new ArgumentException("Bu fonksiyon sadece düz duvar modu için tasarlanmıştır!");
            }

            return GenerateUstDuzDuvar(duvar1, altDizilim);
        }

       
        public List<(int skor, List<(string, int)> dizilim, List<string> log)>
            UretV2Temiz(int duvar1, List<(string, int)> altDizilim = null, int deneme = 100)
        {
            var sonuc = new List<(int skor, List<(string, int)> dizilim, List<string> log)>();
            var benzersizDizilimler = new HashSet<string>();

            for (int i = 0; i < deneme; i++)
            {
                var layout = GenerateUstV2Temiz(duvar1, true, altDizilim);
                if (layout == null || layout.Count == 0) continue;

                // Genişlik kontrolü
                int toplamGenislik = layout.Sum(l => l.Item2);
                if (toplamGenislik > duvar1) continue; // Geçersiz

                // Benzersizlik kontrolü
                string anahtar = string.Join("|", layout.Select(l => $"{l.Item1}:{l.Item2}"));
                if (benzersizDizilimler.Contains(anahtar)) continue;
                benzersizDizilimler.Add(anahtar);

                List<string> log;
                int skor = EvaluateV2Temiz(layout, duvar1, out log);
                sonuc.Add((skor, layout, log));
            }

            return sonuc.OrderByDescending(x => x.skor).ToList();
        }

        
        private int EvaluateV2Temiz(List<(string, int)> layout, int duvarUzunlugu, out List<string> log)
        {
            log = new List<string>();
            int score = 0;

            int toplam = layout.Sum(l => l.Item2);

            // Temel kontroller
            if (toplam > duvarUzunlugu)
            {
                log.Add($"Duvar sınırı aşıldı: {toplam} > {duvarUzunlugu}");
                return -1000;
            }

            // Tam doldurma bonusu
            if (toplam == duvarUzunlugu)
            {
                score += 100;
                log.Add("Tam doldurma bonusu: +100");
            }
            else
            {
                int eksik = duvarUzunlugu - toplam;
                score -= eksik; // Eksik alan cezası
                log.Add($"Eksik alan cezası: -{eksik}");
            }

            // Mevcut değerlendirme kurallarını da ekle
            score += EvaluateUstDolapRules(layout, duvarUzunlugu, log);
            score += EvaluateUserRulesCached(layout, duvarUzunlugu, log);

            return score;
        }


        // Üst dolaplar için basit puanlama sistemi
        public int Evaluate(List<(string, int)> layout, int totalWidth, out List<string> log)
        {
            log = new List<string>();
            int score = 0;

            // Üst dolaplar için özel kurallar
            score += EvaluateUstDolapRules(layout, totalWidth, log);

            // Kullanıcı kuralları (cache'lenmiş)
            score += EvaluateUserRulesCached(layout, totalWidth, log);

            return score;
        }

        // Üst dolaplar için özel kurallar
        private int EvaluateUstDolapRules(List<(string, int)> layout, int totalWidth, List<string> log)
        {
            int score = 0;

            // Tam doldurma kontrolü
            int realTotal = CalculateRealTotalWidth(layout);
            int gap = totalWidth - realTotal;

            if (gap == 0)
            {
                score += 15;
                log.Add("✔️ Üst dolap tam doldurma: +15");
            }
            
            else
            {
                score += 0;
                log.Add($"⚠️ Üst dolap boşluk ({gap}cm): +0");
            }

            // YENİ: Üst dolap sayısı bonusu
            var normalDolaplar = layout.Where(x => x.Item1 == "dolap").Count();
            if (normalDolaplar >= 4)
            {
                score += 8;
                log.Add($"✔️ Çok üst dolap ({normalDolaplar} adet): +8");
            }
            else if (normalDolaplar >= 2)
            {
                score += 5;
                log.Add($"✔️ Yeterli üst dolap ({normalDolaplar} adet): +5");
            }
            var ustDolaplar = layout.Where(x => x.Item1 == "dolap").Select(x => x.Item2).ToList();
            if (ustDolaplar.Count > 1 && ustDolaplar.All(w => w == ustDolaplar[0]))
            {
                score += 5;
                log.Add("✔️ Tüm üst dolaplar aynı ölçüde: +5");
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
                        log.Add($" Üst Dolap AI Kuralı [{userRule.Substring(0, Math.Min(30, userRule.Length))}...]: {(score > 0 ? "+" : "")}{score} puan");
                    }
                    else
                    {
                        log.Add($"❌ Kural bulunamadı: {userRule.Substring(0, Math.Min(20, userRule.Length))}...");
                    }
                }
                catch (Exception ex)
                {
                    log.Add($"❌ Kullanıcı kuralı hatası [{userRule.Substring(0, Math.Min(15, userRule.Length))}...]: {ex.Message}");
                }
            }

            return totalUserScore;
        }

        private async Task<Func<List<(string, int)>, int, int>> CompileUserRuleAsync(string userRule)
        {
            string prompt = $@"
Create a C# method for upper cabinet layout rule evaluation. User rule in Turkish: '{userRule}'

Layout data:
- layout: List<(string, int)> - (module_type, width) pairs
- Module types: ONLY dolap (cabinet) and ust_kose (upper corner) - NO fridge, sink, oven, dishwasher
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
    
    // Find indices (only dolap and ust_kose available)
    var dolapIndices = types.Select((type, index) => type.StartsWith(""dolap"") ? index : -1)
                           .Where(i => i != -1).ToList();
    
    // Implement the Turkish rule logic here for upper cabinets
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

        // Dispose pattern for Groq client
        public void Dispose()
        {
            groqClient?.Dispose();
        }
    }
}