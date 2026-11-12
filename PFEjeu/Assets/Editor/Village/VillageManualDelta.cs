using System.IO;
using UnityEditor;
using UnityEngine;

namespace Village.Layout.EditorTools
{
    /// <summary>
    /// Ecart "terrain sculpte a la main - sculpt recalcule depuis la BASE", enregistre une seule fois.
    /// Reapplique apres le sculpt du relief, il restitue le terrain manuel (butte du village) sans que la
    /// heightmap courante serve jamais de source : la chaine reste idempotente depuis la BASE.
    /// Fichier binaire : resolution, rectangle (i0, j0, largeur, hauteur) puis les ecarts normalises.
    /// </summary>
    public static class VillageManualDelta
    {
        public const string AssetPath = "Assets/Editor/Village/VillageManualDelta.bytes";

        /// <summary>Sous ce seuil (demi-pas d'une heightmap 16 bits) un ecart est considere nul.</summary>
        private const float Epsilon = 0.5f / 65535f;

        public static bool Exists => File.Exists(AssetPath);

        /// <summary>Enregistre l'ecart entre les deux heightmaps (meme resolution). Retourne le nombre d'echantillons non nuls.</summary>
        public static int Save(float[,] current, float[,] sculpted)
        {
            int res = current.GetLength(0);
            if (sculpted.GetLength(0) != res)
                throw new System.InvalidOperationException("Resolutions differentes : terrain " + res + ", sculpt " + sculpted.GetLength(0) + ".");

            int i0 = res, j0 = res, i1 = -1, j1 = -1, count = 0;
            for (int j = 0; j < res; j++)
            {
                for (int i = 0; i < res; i++)
                {
                    if (Mathf.Abs(current[j, i] - sculpted[j, i]) <= Epsilon) continue;
                    count++;
                    if (i < i0) i0 = i;
                    if (i > i1) i1 = i;
                    if (j < j0) j0 = j;
                    if (j > j1) j1 = j;
                }
            }

            int width = count == 0 ? 0 : i1 - i0 + 1;
            int height = count == 0 ? 0 : j1 - j0 + 1;
            using (var writer = new BinaryWriter(File.Create(AssetPath)))
            {
                writer.Write(res);
                writer.Write(count == 0 ? 0 : i0);
                writer.Write(count == 0 ? 0 : j0);
                writer.Write(width);
                writer.Write(height);
                for (int j = 0; j < height; j++)
                {
                    for (int i = 0; i < width; i++)
                    {
                        float d = current[j0 + j, i0 + i] - sculpted[j0 + j, i0 + i];
                        writer.Write(Mathf.Abs(d) <= Epsilon ? 0f : d);
                    }
                }
            }
            AssetDatabase.ImportAsset(AssetPath);
            return count;
        }

        /// <summary>Ajoute l'ecart enregistre aux hauteurs normalisees [j, i]. Retourne faux si absent ou incompatible.</summary>
        public static bool Apply(float[,] heights)
        {
            if (!Exists) return false;

            using (var reader = new BinaryReader(File.OpenRead(AssetPath)))
            {
                int res = reader.ReadInt32();
                if (res != heights.GetLength(0))
                {
                    Debug.LogError("[VillageLayout] Ecart manuel enregistre en " + res + ", sculpt en " + heights.GetLength(0) + " : ecart ignore.");
                    return false;
                }

                int i0 = reader.ReadInt32();
                int j0 = reader.ReadInt32();
                int width = reader.ReadInt32();
                int height = reader.ReadInt32();
                for (int j = 0; j < height; j++)
                {
                    for (int i = 0; i < width; i++)
                    {
                        float d = reader.ReadSingle();
                        if (d != 0f) heights[j0 + j, i0 + i] = Mathf.Clamp01(heights[j0 + j, i0 + i] + d);
                    }
                }
            }
            return true;
        }
    }
}
