using UnityEngine;

namespace Minecraft.Game.Player
{
    /// <summary>
    /// 远程玩家实体。负责在客户端渲染其他在线玩家的简化形象（胶囊体 + 名牌），
    /// 并平滑插值同步服务端下发的位置与朝向。
    /// <para>
    /// 渲染方式：
    /// - 胶囊体（<see cref="PrimitiveType.Capsule"/>）：代表玩家身体，使用彩色材质区分。
    /// - 名牌（<see cref="TextMesh"/>）：头顶显示角色名，始终面向相机。
    /// </para>
    /// <para>
    /// 平滑策略：使用 <see cref="Vector3.Lerp"/> 与 <see cref="Quaternion.Slerp"/> 插值，
    /// 避免网络抖动导致的瞬移感。插值速度可配置。
    /// </para>
    /// </summary>
    public class RemotePlayerEntity : MonoBehaviour
    {
        // ==================== 配置常量 ====================

        /// <summary>胶囊体高度（与本地玩家身高一致）。</summary>
        private const float CapsuleHeight = 1.8f;

        /// <summary>名牌距头顶的偏移量。</summary>
        private const float NameTagOffset = 2.2f;

        /// <summary>位置插值平滑系数（值越大越快跟随）。</summary>
        private const float PositionLerpSpeed = 12f;

        /// <summary>朝向插值平滑系数。</summary>
        private const float RotationLerpSpeed = 10f;

        // ==================== 运行时状态 ====================

        /// <summary>实体 ID（服务端分配）。</summary>
        public long EntityId { get; private set; }

        /// <summary>角色名。</summary>
        public string PlayerName { get; private set; }

        /// <summary>目标位置（服务端最新下发的位置）。</summary>
        private Vector3 _targetPosition;

        /// <summary>目标朝向（Yaw 角度）。</summary>
        private float _targetYaw;

        /// <summary>名牌 TextMesh 引用。</summary>
        private TextMesh _nameTag;

        /// <summary>是否已初始化。</summary>
        private bool _initialized;

        // ==================== 公开方法 ====================

        /// <summary>
        /// 初始化远程实体。创建渲染几何体与名牌，设置初始位置。
        /// </summary>
        /// <param name="entityId">实体 ID。</param>
        /// <param name="playerName">角色名。</param>
        /// <param name="startPosition">初始世界坐标。</param>
        /// <param name="startYaw">初始朝向（Yaw 角度）。</param>
        /// <param name="bodyColor">身体颜色。</param>
        public void Initialize(long entityId, string playerName, Vector3 startPosition,
            float startYaw, Color bodyColor)
        {
            EntityId = entityId;
            PlayerName = playerName;

            CreateBody(bodyColor);
            CreateNameTag(playerName);

            _targetPosition = startPosition;
            _targetYaw = startYaw;

            transform.position = startPosition;
            transform.rotation = Quaternion.Euler(0f, startYaw, 0f);

            _initialized = true;
        }

        /// <summary>
        /// 更新目标位置与朝向（由 <see cref="RemoteEntityManager"/> 调用）。
        /// </summary>
        /// <param name="position">新位置。</param>
        /// <param name="yaw">新朝向（Yaw 角度）。</param>
        public void UpdateTarget(Vector3 position, float yaw)
        {
            _targetPosition = position;
            _targetYaw = yaw;
        }

        // ==================== Unity 生命周期 ====================

        /// <summary>每帧插值移动到目标位置与朝向。</summary>
        private void Update()
        {
            if (!_initialized)
                return;

            float dt = Time.deltaTime;

            // 位置插值
            transform.position = Vector3.Lerp(
                transform.position, _targetPosition, dt * PositionLerpSpeed);

            // 朝向插值
            Quaternion targetRot = Quaternion.Euler(0f, _targetYaw, 0f);
            transform.rotation = Quaternion.Slerp(
                transform.rotation, targetRot, dt * RotationLerpSpeed);

            // 名牌面向相机
            if (_nameTag != null && Camera.main != null)
            {
                _nameTag.transform.rotation = Camera.main.transform.rotation;
            }
        }

        // ==================== 内部构建 ====================

        /// <summary>创建胶囊体身体。</summary>
        private void CreateBody(Color color)
        {
            var body = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            body.name = "Body";
            body.transform.SetParent(transform, false);
            body.transform.localScale = new Vector3(0.6f, CapsuleHeight * 0.5f, 0.6f);
            body.transform.localPosition = new Vector3(0f, CapsuleHeight * 0.5f, 0f);

            // 移除自带的 Collider（远程实体不需要碰撞）
            var collider = body.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            // 设置颜色材质
            var renderer = body.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");

                var mat = new Material(shader) { color = color };
                renderer.sharedMaterial = mat;
            }
        }

        /// <summary>创建头顶名牌。</summary>
        private void CreateNameTag(string name)
        {
            var tagGo = new GameObject("NameTag");
            tagGo.transform.SetParent(transform, false);
            tagGo.transform.localPosition = new Vector3(0f, NameTagOffset, 0f);

            _nameTag = tagGo.AddComponent<TextMesh>();
            _nameTag.text = name;
            _nameTag.fontSize = 24;
            _nameTag.characterSize = 0.15f;
            _nameTag.anchor = TextAnchor.MiddleCenter;
            _nameTag.color = Color.white;

            // 添加 MeshRenderer 使 TextMesh 可见
            var renderer = tagGo.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                renderer.sortingOrder = 100; // 确保名牌渲染在物体上方
            }
        }
    }
}
