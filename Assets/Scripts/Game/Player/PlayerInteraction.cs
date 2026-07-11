using UnityEngine;
using Minecraft.Game.World;

namespace Minecraft.Game.Player
{
    /// <summary>
    /// 方块交互系统。负责射线选中方块、左键破坏、右键放置、方块高亮显示。
    /// <para>
    /// 射线检测使用 <see cref="BlockRaycaster"/>（DDA 算法），而非 <see cref="Physics"/>，
    /// 因为方块世界不使用 Collider（性能考虑）。
    /// </para>
    /// <para>
    /// 简易快捷栏：9 个槽位，数字键 1~9 切换，鼠标滚轮循环。
    /// 破坏方块后自动加入快捷栏（简化版"掉落物拾取"）。
    /// </para>
    /// </summary>
    public class PlayerInteraction : MonoBehaviour
    {
        // ==================== 配置字段 ====================

        /// <summary>交互距离（射线最大长度，单位：格）。</summary>
        [SerializeField] private float reachDistance = 5f;

        /// <summary>选中方块的高亮颜色。</summary>
        [SerializeField] private Color highlightColor = Color.yellow;

        /// <summary>区块管理器（注入）。若未指定，运行时自动查找。</summary>
        [SerializeField] private ChunkManager chunkManager;

        // ==================== 玩家尺寸（用于放置碰撞检测）====================

        /// <summary>玩家碰撞半径（与 PlayerController 保持一致）。</summary>
        private const float PlayerRadius = 0.3f;

        /// <summary>玩家身高（与 PlayerController 保持一致）。</summary>
        private const float PlayerHeight = 1.8f;

        /// <summary>高亮线框的扩展量（避免与方块表面重叠产生 Z-fighting）。</summary>
        private const float HighlightPadding = 0.002f;

        /// <summary>高亮线框宽度。</summary>
        private const float HighlightLineWidth = 0.03f;

        // ==================== 快捷栏 ====================

        /// <summary>快捷栏槽位数。</summary>
        private const int HotbarSize = 9;

        /// <summary>快捷栏方块类型数组。</summary>
        private readonly BlockType[] _hotbar = new BlockType[HotbarSize]
        {
            BlockType.Grass,
            BlockType.Dirt,
            BlockType.Stone,
            BlockType.Cobblestone,
            BlockType.Planks,
            BlockType.Wood,
            BlockType.Sand,
            BlockType.Glass,
            BlockType.Leaves,
        };

        /// <summary>快捷栏各方块数量。</summary>
        private readonly int[] _hotbarCounts = new int[HotbarSize]
        {
            32, 32, 32, 32, 32, 32, 32, 32, 32
        };

        /// <summary>当前选中的快捷栏槽位索引。</summary>
        private int _selectedSlot;

        // ==================== 运行时状态 ====================

        /// <summary>相机引用（射线发射起点）。</summary>
        private Camera _camera;

        /// <summary>高亮线框 LineRenderer。</summary>
        private LineRenderer _highlightRenderer;

        /// <summary>当前射线命中信息。</summary>
        private BlockRaycastHit _currentHit;

        /// <summary>当前是否命中方块。</summary>
        private bool _hasHit;

        // ==================== 公开属性 ====================

        /// <summary>当前选中的快捷栏槽位索引。</summary>
        public int SelectedSlot => _selectedSlot;

        /// <summary>当前选中槽位的方块类型。</summary>
        public BlockType SelectedBlock => _hotbar[_selectedSlot];

        /// <summary>当前选中槽位的方块数量。</summary>
        public int SelectedCount => _hotbarCounts[_selectedSlot];

        /// <summary>当前是否命中方块。</summary>
        public bool HasTarget => _hasHit;

        /// <summary>当前命中信息。</summary>
        public BlockRaycastHit CurrentHit => _currentHit;

        // ==================== Unity 生命周期 ====================

        /// <summary>
        /// 初始化：查找相机、区块管理器，创建高亮线框。
        /// </summary>
        private void Awake()
        {
            // 查找相机（PlayerController 的子物体）
            _camera = GetComponentInChildren<Camera>();
            if (_camera == null)
                _camera = Camera.main;

            // 自动查找区块管理器
            if (chunkManager == null)
                chunkManager = FindObjectOfType<ChunkManager>();

            SetupHighlight();
        }

        /// <summary>每帧更新：快捷栏选择、射线检测、方块交互。</summary>
        private void Update()
        {
            if (chunkManager == null || _camera == null)
                return;

            HandleHotbarSelection();
            UpdateTargetedBlock();
            HandleBlockInteraction();
        }

        // ==================== 快捷栏 ====================

        /// <summary>
        /// 处理快捷栏选择：数字键 1~9 直接选择，鼠标滚轮循环切换。
        /// </summary>
        private void HandleHotbarSelection()
        {
            // 数字键 1~9 直接选择对应槽位
            for (int i = 0; i < HotbarSize; i++)
            {
                if (Input.GetKeyDown(KeyCode.Alpha1 + i))
                {
                    _selectedSlot = i;
                    return;
                }
            }

            // 鼠标滚轮循环切换
            float scroll = Input.GetAxis("Mouse ScrollWheel");
            if (scroll > 0f)
            {
                // 向上滚：上一个槽位
                _selectedSlot = (_selectedSlot + HotbarSize - 1) % HotbarSize;
            }
            else if (scroll < 0f)
            {
                // 向下滚：下一个槽位
                _selectedSlot = (_selectedSlot + 1) % HotbarSize;
            }
        }

        /// <summary>
        /// 获取指定槽位的方块类型和数量。
        /// </summary>
        /// <param name="slot">槽位索引（0~8）。</param>
        /// <param name="type">输出：方块类型。</param>
        /// <param name="count">输出：方块数量。</param>
        public void GetHotbarSlot(int slot, out BlockType type, out int count)
        {
            type = _hotbar[slot];
            count = _hotbarCounts[slot];
        }

        // ==================== 射线检测 ====================

        /// <summary>
        /// 从相机发射射线，更新当前命中方块信息。
        /// 同时更新高亮线框显示。
        /// </summary>
        private void UpdateTargetedBlock()
        {
            Vector3 origin = _camera.transform.position;
            Vector3 direction = _camera.transform.forward;

            _hasHit = BlockRaycaster.Raycast(origin, direction, reachDistance,
                chunkManager, out _currentHit);

            if (_hasHit)
                ShowHighlight(_currentHit.BlockPosition);
            else
                HideHighlight();
        }

        // ==================== 方块交互 ====================

        /// <summary>
        /// 处理左键破坏、右键放置。
        /// </summary>
        private void HandleBlockInteraction()
        {
            if (!_hasHit)
                return;

            // 左键：破坏方块
            if (Input.GetMouseButtonDown(0))
            {
                BreakBlock(_currentHit);
            }

            // 右键：放置方块
            if (Input.GetMouseButtonDown(1))
            {
                PlaceBlock(_currentHit);
            }
        }

        /// <summary>
        /// 破坏选中方块。基岩不可破坏。破坏后将方块加入快捷栏（简化版掉落物拾取）。
        /// </summary>
        /// <param name="hit">射线命中信息。</param>
        private void BreakBlock(BlockRaycastHit hit)
        {
            BlockType brokenType = hit.BlockType;

            // 基岩不可破坏
            if (brokenType == BlockType.Bedrock)
                return;

            // 设为空气（销毁方块）
            Vector3Int pos = hit.BlockPosition;
            chunkManager.SetBlock(pos.x, pos.y, pos.z, BlockType.Air);

            // 简化处理：直接将方块加入快捷栏
            AddToInventory(brokenType);
        }

        /// <summary>
        /// 在命中面的相邻位置放置方块。
        /// 放置位置 = 命中方块坐标 + 命中面法线方向。
        /// 若目标位置已有固体方块或会与玩家重叠，则取消放置。
        /// </summary>
        /// <param name="hit">射线命中信息。</param>
        private void PlaceBlock(BlockRaycastHit hit)
        {
            // 检查选中槽位是否有方块
            if (_hotbarCounts[_selectedSlot] <= 0)
                return;

            BlockType placeType = _hotbar[_selectedSlot];

            // 计算放置位置（命中方块的相邻面）
            Vector3Int placePos = hit.BlockPosition + hit.Normal;

            // 目标位置已有固体方块，不覆盖
            if (BlockDefinition.IsSolid(chunkManager.GetBlock(placePos.x, placePos.y, placePos.z)))
                return;

            // 放置位置与玩家重叠，取消放置（避免把自己卡进方块）
            if (WouldOverlapPlayer(placePos))
                return;

            // 放置方块并消耗一个
            chunkManager.SetBlock(placePos.x, placePos.y, placePos.z, placeType);
            _hotbarCounts[_selectedSlot]--;
        }

        // ==================== 背包逻辑 ====================

        /// <summary>
        /// 将方块加入快捷栏。优先堆叠到同类型槽位，其次放入空槽位。
        /// 快捷栏已满则丢弃。
        /// </summary>
        /// <param name="type">要加入的方块类型。</param>
        private void AddToInventory(BlockType type)
        {
            // 优先堆叠到已有同类型方块的槽位
            for (int i = 0; i < HotbarSize; i++)
            {
                if (_hotbar[i] == type && _hotbarCounts[i] > 0)
                {
                    _hotbarCounts[i]++;
                    return;
                }
            }

            // 放入空槽位（数量为 0 或类型为 Air）
            for (int i = 0; i < HotbarSize; i++)
            {
                if (_hotbarCounts[i] <= 0 || _hotbar[i] == BlockType.Air)
                {
                    _hotbar[i] = type;
                    _hotbarCounts[i] = 1;
                    return;
                }
            }

            // 快捷栏已满，丢弃
        }

        // ==================== 碰撞检测 ====================

        /// <summary>
        /// 检查指定方块位置是否与玩家碰撞体重叠。
        /// 防止放置方块时把自己卡进方块内部。
        /// </summary>
        /// <param name="blockPos">方块世界整数坐标。</param>
        /// <returns>是否重叠。</returns>
        private bool WouldOverlapPlayer(Vector3Int blockPos)
        {
            Vector3 playerPos = transform.position;

            // 玩家 AABB（近似 CharacterController 碰撞体）
            float pMinX = playerPos.x - PlayerRadius;
            float pMaxX = playerPos.x + PlayerRadius;
            float pMinY = playerPos.y;
            float pMaxY = playerPos.y + PlayerHeight;
            float pMinZ = playerPos.z - PlayerRadius;
            float pMaxZ = playerPos.z + PlayerRadius;

            // 方块 AABB（[x, x+1] × [y, y+1] × [z, z+1]）
            float bMinX = blockPos.x;
            float bMaxX = blockPos.x + 1f;
            float bMinY = blockPos.y;
            float bMaxY = blockPos.y + 1f;
            float bMinZ = blockPos.z;
            float bMaxZ = blockPos.z + 1f;

            // AABB 重叠判断：三轴都有交集
            return pMaxX > bMinX && pMinX < bMaxX
                && pMaxY > bMinY && pMinY < bMaxY
                && pMaxZ > bMinZ && pMinZ < bMaxZ;
        }

        // ==================== 高亮线框 ====================

        /// <summary>
        /// 创建高亮线框 GameObject（LineRenderer）。
        /// 使用 16 个顶点绘制立方体的 12 条边（3 条边重复以保持连线）。
        /// </summary>
        private void SetupHighlight()
        {
            var highlightObj = new GameObject("BlockHighlight");
            highlightObj.transform.SetParent(transform);

            _highlightRenderer = highlightObj.AddComponent<LineRenderer>();
            _highlightRenderer.startWidth = HighlightLineWidth;
            _highlightRenderer.endWidth = HighlightLineWidth;
            _highlightRenderer.positionCount = 0;
            _highlightRenderer.useWorldSpace = true;
            _highlightRenderer.loop = false;

            // 使用 Unlit/Color 着色器，颜色不受光照影响
            Shader shader = Shader.Find("Unlit/Color");
            if (shader != null)
            {
                _highlightRenderer.material = new Material(shader) { color = highlightColor };
            }

            _highlightRenderer.gameObject.SetActive(false);
        }

        /// <summary>
        /// 在指定方块位置显示高亮线框。
        /// 线框为略大于 1×1×1 的立方体，包覆在方块外围。
        /// </summary>
        /// <param name="blockPos">方块世界整数坐标。</param>
        private void ShowHighlight(Vector3Int blockPos)
        {
            if (_highlightRenderer == null)
                return;

            _highlightRenderer.gameObject.SetActive(true);

            // 线框的 8 个顶点（立方体角点，略向外扩展避免 Z-fighting）
            float pad = HighlightPadding;
            float minX = blockPos.x - pad;
            float maxX = blockPos.x + 1f + pad;
            float minY = blockPos.y - pad;
            float maxY = blockPos.y + 1f + pad;
            float minZ = blockPos.z - pad;
            float maxZ = blockPos.z + 1f + pad;

            // 8 个角点
            Vector3 v0 = new Vector3(minX, minY, minZ);
            Vector3 v1 = new Vector3(maxX, minY, minZ);
            Vector3 v2 = new Vector3(maxX, minY, maxZ);
            Vector3 v3 = new Vector3(minX, minY, maxZ);
            Vector3 v4 = new Vector3(minX, maxY, minZ);
            Vector3 v5 = new Vector3(maxX, maxY, minZ);
            Vector3 v6 = new Vector3(maxX, maxY, maxZ);
            Vector3 v7 = new Vector3(minX, maxY, maxZ);

            // 16 个顶点的路径，覆盖全部 12 条边（3 条边重复以保持连线，无跳跃线段）
            _highlightRenderer.positionCount = 16;
            _highlightRenderer.SetPositions(new Vector3[]
            {
                // 底面 4 条边
                v0, v1, v2, v3, v0,
                // 上升至顶面 + 顶面 4 条边
                v4, v5, v6, v7, v4,
                // 剩余 3 条垂直边（途经已绘制的边以保持连线）
                v5, v1, v2, v6, v7, v3
            });
        }

        /// <summary>隐藏高亮线框。</summary>
        private void HideHighlight()
        {
            if (_highlightRenderer != null)
                _highlightRenderer.gameObject.SetActive(false);
        }
    }
}
