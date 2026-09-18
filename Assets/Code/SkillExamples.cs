// SkillExamples.cs — chỉ giữ lại SkillUtils dùng chung.
// Tất cả các class skill đã được tách ra file riêng (Unity yêu cầu tên file = tên class).
using System.Collections.Generic;
using UnityEngine;

namespace LoRClone.Skills
{
    internal static class SkillUtils
    {
        public static void Shuffle<T>(List<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = Random.Range(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }
}