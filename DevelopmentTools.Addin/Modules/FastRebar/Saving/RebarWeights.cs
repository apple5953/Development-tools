// 鋼筋單位重量（kg/m）。CNS 560 竹節鋼筋標稱值（D10～D57，對照東和鋼鐵產品型錄）；查不到的直徑以鋼材密度 7850 kg/m³ 計算。
using System;

namespace DevelopmentTools.FastRebar.Saving
{
    public static class RebarWeights
    {
        // { 標稱直徑 mm, 單位重量 kg/m }
        private static readonly double[,] Cns560 =
        {
            { 9.53, 0.560 }, { 12.7, 0.994 }, { 15.9, 1.56 }, { 19.1, 2.25 },
            { 22.2, 3.04 }, { 25.4, 3.98 }, { 28.7, 5.08 }, { 32.2, 6.39 },
            { 35.8, 7.90 }, { 39.4, 9.57 }, { 43.0, 11.4 }, { 50.2, 15.5 }, { 57.3, 20.2 },
        };

        public static double KgPerM(double diameterMm)
        {
            for (int i = 0; i < Cns560.GetLength(0); i++)
            {
                if (Math.Abs(Cns560[i, 0] - diameterMm) <= 0.5) return Cns560[i, 1];
            }
            return 7850.0 * Math.PI * diameterMm * diameterMm / 4.0 / 1e6;
        }
    }
}
