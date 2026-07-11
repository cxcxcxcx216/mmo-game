// =====================================================================
// 此文件由 config_util 工具自动生成，请勿手动修改。
// 生成时间：2026-07-11
// 源文件：config_util/excel/MMOConfig.xlsx → Sheet: ProfessionConfig
// =====================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Minecraft.Config
{
    [Serializable]
    public class ProfessionConfig
    {
        public int Id; // int
        public string Name; // string
        public string Description; // string
        public int BaseHp; // int
        public int BaseAtk; // int
        public int BaseDef; // int
        public float BaseSpeed; // float
    }

    [Serializable]
    public class ProfessionConfigContainer
    {
        public List<ProfessionConfig> rows;
    }

    public class ProfessionConfigManager
    {
        private Dictionary<int, ProfessionConfig> _data = new Dictionary<int, ProfessionConfig>();

        public void Load(string json)
        {
            var container = JsonUtility.FromJson<ProfessionConfigContainer>("{\"rows\":" + json + "}");
            _data.Clear();
            if (container != null && container.rows != null)
            {
                foreach (var row in container.rows)
                {
                    _data[row.Id] = row;
                }
            }
        }

        public ProfessionConfig GetById(int id)
        {
            _data.TryGetValue(id, out var val);
            return val;
        }

        public Dictionary<int, ProfessionConfig>.ValueCollection GetAll()
        {
            return _data.Values;
        }

        public void Reload(string json)
        {
            _data.Clear();
            Load(json);
        }
    }
}
