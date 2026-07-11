// =====================================================================
// 此文件由 config_util 工具自动生成，请勿手动修改。
// 生成时间：2026-07-11
// 源文件：config_util/excel/MMOConfig.xlsx → Sheet: BlockConfig
// =====================================================================
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Minecraft.Config
{
    [Serializable]
    public class BlockConfig
    {
        public int Id; // int
        public string Name; // string
        public string Category; // string
        public float Hardness; // float
        public bool Transparent; // bool
        public string Color; // string
    }

    [Serializable]
    public class BlockConfigContainer
    {
        public List<BlockConfig> rows;
    }

    public class BlockConfigManager
    {
        private Dictionary<int, BlockConfig> _data = new Dictionary<int, BlockConfig>();

        public void Load(string json)
        {
            var container = JsonUtility.FromJson<BlockConfigContainer>("{\"rows\":" + json + "}");
            _data.Clear();
            if (container != null && container.rows != null)
            {
                foreach (var row in container.rows)
                {
                    _data[row.Id] = row;
                }
            }
        }

        public BlockConfig GetById(int id)
        {
            _data.TryGetValue(id, out var val);
            return val;
        }

        public Dictionary<int, BlockConfig>.ValueCollection GetAll()
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
