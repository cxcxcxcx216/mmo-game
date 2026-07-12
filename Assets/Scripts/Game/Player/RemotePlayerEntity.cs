using UnityEngine;

namespace Minecraft.Game.Player
{
    /// <summary>
    /// 远程实体（NPC / 远程玩家）。由基本图元拼装人形模型并播放简单动画，
    /// 平滑插值同步服务端下发的位置、朝向与速度。
    /// <para>
    /// 模型结构（model-local，y=0 为脚底）：
    /// - 头部 Cube(0.5)，y=1.5+GroundOffset
    /// - 身体 Cube(0.6×0.8×0.3)，y=0.9+GroundOffset
    /// - 左/右臂 Cube(0.25×0.8×0.25)，pivot 在肩部
    /// - 左/右腿 Cube(0.3×0.8×0.3)，pivot 在臀部
    /// </para>
    /// <para>
    /// 动画纯代码驱动：
    /// - 行走：腿/臂绕 X 轴正弦摆动，频率 = speed × 2 Hz。
    /// - 待机：身体轻微上下呼吸。
    /// </para>
    /// </summary>
    public class RemotePlayerEntity : MonoBehaviour
    {
        // ==================== 模型尺寸常量 ====================

        private const float HeadSize = 0.5f;
        private const float HeadY = 1.5f;

        private const float BodyWidth = 0.6f;
        private const float BodyHeight = 0.8f;
        private const float BodyDepth = 0.3f;
        private const float BodyY = 0.9f;

        private const float ArmWidth = 0.25f;
        private const float ArmHeight = 0.8f;
        private const float ArmDepth = 0.25f;
        private const float ArmY = 0.9f;

        private const float LegWidth = 0.3f;
        private const float LegHeight = 0.8f;
        private const float LegDepth = 0.3f;
        private const float LegY = 0.0f;

        /// <summary>手臂 X 偏移 = 身体半宽 + 手臂半宽。</summary>
        private const float ArmSideOffset = 0.425f;

        /// <summary>腿部 X 偏移。</summary>
        private const float LegSideOffset = 0.15f;

        /// <summary>模型整体上移量，使脚底位于 entity-local y=0（LegHeight/2）。</summary>
        private const float ModelGroundOffset = 0.4f;

        // ==================== 名牌常量 ====================

        private const float NameTagOffset = 2.2f;
        private const float NameTagHideDistance = 50f;
        private const float NameTagMainSize = 0.15f;
        private const float NameTagOutlineSize = 0.17f;
        private const float NameTagOutlineZOffset = -0.02f;

        // ==================== 动画常量 ====================

        private const float WalkLegAmplitudeDeg = 30f;
        private const float WalkArmAmplitudeDeg = 20f;
        private const float WalkFreqHzPerSpeed = 2f;
        private const float WalkSpeedThreshold = 0.1f;
        private const float IdleBreathAmplitude = 0.03f;
        private const float IdleBreathFreqHz = 1.5f;
        private const float IdleLerpSpeed = 10f;

        // ==================== 插值常量 ====================

        private const float PositionLerpSpeed = 12f;
        private const float RotationLerpSpeed = 10f;

        // ==================== 颜色调色常量 ====================

        private const float HeadBrightenAmount = 0.25f;
        private const float LimbDarkenAmount = 0.3f;

        /// <summary>NPC 缩放比例。</summary>
        private const float NpcScale = 0.9f;

        // ==================== 运行时状态 ====================

        public long EntityId { get; private set; }
        public string PlayerName { get; private set; }

        private Vector3 _targetPosition;
        private float _targetYaw;
        private float _currentSpeed;
        private float _animPhase;
        private float _idleTime;
        private bool _initialized;

        // 模型部位引用（用于动画）
        private Transform _bodyTransform;
        private Transform _leftArmPivot;
        private Transform _rightArmPivot;
        private Transform _leftLegPivot;
        private Transform _rightLegPivot;
        private Transform _nameTagRoot;

        // ==================== 公开方法 ====================

        /// <summary>
        /// 初始化远程实体：创建人形模型、名牌，设置初始位姿。
        /// </summary>
        /// <param name="entityId">实体 ID。</param>
        /// <param name="playerName">角色名。</param>
        /// <param name="startPosition">初始世界坐标（脚底）。</param>
        /// <param name="startYaw">初始朝向（Yaw 角度）。</param>
        /// <param name="speed">初始速度（用于动画）。</param>
        /// <param name="bodyColor">身体颜色。</param>
        /// <param name="isNpc">是否为 NPC（影响名牌前缀与缩放）。</param>
        public void Initialize(long entityId, string playerName, Vector3 startPosition,
            float startYaw, float speed, Color bodyColor, bool isNpc)
        {
            EntityId = entityId;
            PlayerName = playerName;
            _currentSpeed = speed;

            CreateHumanoidModel(bodyColor);

            string tagName = isNpc ? "[NPC] " + playerName : playerName;
            CreateNameTag(tagName);

            if (isNpc)
                transform.localScale = Vector3.one * NpcScale;

            _targetPosition = startPosition;
            _targetYaw = startYaw;

            transform.position = startPosition;
            transform.rotation = Quaternion.Euler(0f, startYaw, 0f);

            _initialized = true;
        }

        /// <summary>
        /// 更新目标位置、朝向与速度（由 <see cref="RemoteEntityManager"/> 调用）。
        /// </summary>
        public void UpdateTarget(Vector3 position, float yaw, float speed)
        {
            _targetPosition = position;
            _targetYaw = yaw;
            _currentSpeed = speed;
        }

        // ==================== Unity 生命周期 ====================

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

            UpdateAnimation(dt);
            UpdateNameTag();
        }

        // ==================== 动画 ====================

        /// <summary>根据速度选择行走 / 待机动画。</summary>
        private void UpdateAnimation(float dt)
        {
            _idleTime += dt;

            if (_currentSpeed > WalkSpeedThreshold)
            {
                // 行走：腿臂正弦摆动，频率 = speed * WalkFreqHzPerSpeed Hz
                _animPhase += dt * Mathf.PI * 2f * (_currentSpeed * WalkFreqHzPerSpeed);

                float armSwing = Mathf.Sin(_animPhase) * WalkArmAmplitudeDeg;
                float legSwing = Mathf.Sin(_animPhase) * WalkLegAmplitudeDeg;

                // 左臂与右腿同向，右臂与左腿同向（自然交替）
                _leftArmPivot.localRotation = Quaternion.Euler(armSwing, 0f, 0f);
                _rightArmPivot.localRotation = Quaternion.Euler(-armSwing, 0f, 0f);
                _leftLegPivot.localRotation = Quaternion.Euler(-legSwing, 0f, 0f);
                _rightLegPivot.localRotation = Quaternion.Euler(legSwing, 0f, 0f);

                // 行走时身体回到基准高度
                if (_bodyTransform != null)
                    _bodyTransform.localPosition = new Vector3(0f, BodyY, 0f);
            }
            else
            {
                // 待机：身体呼吸微浮
                float breath = Mathf.Sin(_idleTime * Mathf.PI * 2f * IdleBreathFreqHz) * IdleBreathAmplitude;
                if (_bodyTransform != null)
                    _bodyTransform.localPosition = new Vector3(0f, BodyY + breath, 0f);

                // 四肢平滑回到自然下垂
                _leftArmPivot.localRotation = Quaternion.Lerp(
                    _leftArmPivot.localRotation, Quaternion.identity, dt * IdleLerpSpeed);
                _rightArmPivot.localRotation = Quaternion.Lerp(
                    _rightArmPivot.localRotation, Quaternion.identity, dt * IdleLerpSpeed);
                _leftLegPivot.localRotation = Quaternion.Lerp(
                    _leftLegPivot.localRotation, Quaternion.identity, dt * IdleLerpSpeed);
                _rightLegPivot.localRotation = Quaternion.Lerp(
                    _rightLegPivot.localRotation, Quaternion.identity, dt * IdleLerpSpeed);
            }
        }

        /// <summary>名牌面向相机，距离过远时隐藏。</summary>
        private void UpdateNameTag()
        {
            if (_nameTagRoot == null)
                return;

            Camera cam = Camera.main;
            if (cam == null)
                return;

            float dist = Vector3.Distance(transform.position, cam.transform.position);
            bool visible = dist <= NameTagHideDistance;
            _nameTagRoot.gameObject.SetActive(visible);

            if (visible)
                _nameTagRoot.rotation = cam.transform.rotation;
        }

        // ==================== 模型构建 ====================

        /// <summary>
        /// 创建人形模型。头部作为身体子对象（跟随呼吸），手臂/腿以 pivot 方式挂载
        /// 以便绕关节旋转。
        /// </summary>
        private void CreateHumanoidModel(Color bodyColor)
        {
            // 模型根节点：整体上移使脚底着地
            var modelRoot = new GameObject("Model");
            modelRoot.transform.SetParent(transform, false);
            modelRoot.transform.localPosition = new Vector3(0f, ModelGroundOffset, 0f);

            // 颜色变体：头部稍亮，四肢稍暗
            Color headColor = Color.Lerp(bodyColor, Color.white, HeadBrightenAmount);
            Color limbColor = Color.Lerp(bodyColor, Color.black, LimbDarkenAmount);

            // 身体
            _bodyTransform = CreateCube("Body",
                new Vector3(BodyWidth, BodyHeight, BodyDepth),
                new Vector3(0f, BodyY, 0f),
                bodyColor, modelRoot.transform).transform;

            // 头部（身体子对象，呼吸时跟随上下）
            CreateCube("Head",
                new Vector3(HeadSize, HeadSize, HeadSize),
                new Vector3(0f, HeadY - BodyY, 0f),
                headColor, _bodyTransform);

            // 手臂：pivot 在肩部（ArmY + ArmHeight/2），mesh 下垂
            _leftArmPivot = CreateLimb("LeftArm",
                new Vector3(ArmWidth, ArmHeight, ArmDepth),
                new Vector3(-ArmSideOffset, ArmY + ArmHeight * 0.5f, 0f),
                new Vector3(0f, -ArmHeight * 0.5f, 0f),
                limbColor, modelRoot.transform).transform;

            _rightArmPivot = CreateLimb("RightArm",
                new Vector3(ArmWidth, ArmHeight, ArmDepth),
                new Vector3(ArmSideOffset, ArmY + ArmHeight * 0.5f, 0f),
                new Vector3(0f, -ArmHeight * 0.5f, 0f),
                limbColor, modelRoot.transform).transform;

            // 腿：pivot 在臀部（LegY + LegHeight/2），mesh 下垂
            _leftLegPivot = CreateLimb("LeftLeg",
                new Vector3(LegWidth, LegHeight, LegDepth),
                new Vector3(-LegSideOffset, LegY + LegHeight * 0.5f, 0f),
                new Vector3(0f, -LegHeight * 0.5f, 0f),
                limbColor, modelRoot.transform).transform;

            _rightLegPivot = CreateLimb("RightLeg",
                new Vector3(LegWidth, LegHeight, LegDepth),
                new Vector3(LegSideOffset, LegY + LegHeight * 0.5f, 0f),
                new Vector3(0f, -LegHeight * 0.5f, 0f),
                limbColor, modelRoot.transform).transform;
        }

        /// <summary>创建一个 Cube 图元，设置尺寸 / 位置 / 颜色，移除自带 Collider。</summary>
        private GameObject CreateCube(string name, Vector3 size, Vector3 localPos, Color color, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localScale = size;
            go.transform.localPosition = localPos;

            var collider = go.GetComponent<Collider>();
            if (collider != null)
                Destroy(collider);

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Standard");
                if (shader == null)
                    shader = Shader.Find("Unlit/Color");
                var mat = new Material(shader) { color = color };
                // 漫反射：无金属感、无光泽，自然哑光表面
                mat.SetFloat("_Metallic", 0f);
                mat.SetFloat("_Glossiness", 0f);
                renderer.sharedMaterial = mat;
            }

            return go;
        }

        /// <summary>创建带 pivot 的肢体：pivot 位于关节处，Cube mesh 作为子对象下移半个肢长。</summary>
        private GameObject CreateLimb(string name, Vector3 size, Vector3 pivotPos, Vector3 meshOffset, Color color, Transform parent)
        {
            var pivot = new GameObject(name + "_Pivot");
            pivot.transform.SetParent(parent, false);
            pivot.transform.localPosition = pivotPos;

            CreateCube(name, size, meshOffset, color, pivot.transform);
            return pivot;
        }

        // ==================== 名牌构建 ====================

        /// <summary>创建双层描边名牌：黑色稍大在后 + 白色主文字在前。</summary>
        private void CreateNameTag(string displayName)
        {
            var tagRoot = new GameObject("NameTag");
            tagRoot.transform.SetParent(transform, false);
            tagRoot.transform.localPosition = new Vector3(0f, NameTagOffset, 0f);
            _nameTagRoot = tagRoot.transform;

            // 黑色描边层
            var outlineGo = new GameObject("NameTag_Outline");
            outlineGo.transform.SetParent(tagRoot.transform, false);
            outlineGo.transform.localPosition = new Vector3(0f, 0f, NameTagOutlineZOffset);
            var outline = outlineGo.AddComponent<TextMesh>();
            outline.text = displayName;
            outline.fontSize = 24;
            outline.characterSize = NameTagOutlineSize;
            outline.anchor = TextAnchor.MiddleCenter;
            outline.color = Color.black;
            EnsureRenderer(outlineGo, 99);

            // 白色主文字层
            var mainGo = new GameObject("NameTag_Main");
            mainGo.transform.SetParent(tagRoot.transform, false);
            mainGo.transform.localPosition = Vector3.zero;
            var main = mainGo.AddComponent<TextMesh>();
            main.text = displayName;
            main.fontSize = 24;
            main.characterSize = NameTagMainSize;
            main.anchor = TextAnchor.MiddleCenter;
            main.color = Color.white;
            EnsureRenderer(mainGo, 100);
        }

        /// <summary>确保 TextMesh 所在 GameObject 有 MeshRenderer 并设置 sortingOrder。</summary>
        private static void EnsureRenderer(GameObject go, int sortingOrder)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null)
                renderer = go.AddComponent<MeshRenderer>();
            renderer.sortingOrder = sortingOrder;
        }
    }
}
