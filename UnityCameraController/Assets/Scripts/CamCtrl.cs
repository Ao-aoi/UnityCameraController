using UnityEngine;
using UnityEngine.UI;
#if UNITY_EDITOR
using UnityEditor;
#endif

//画面上の指定領域にマウスカーソルを置くだけで、カメラが滑らかに加速移動・回転する直感的な操作システム
public class CamCtrl : MonoBehaviour
{
#region Fields
    [Header("操作領域設定（エッジレイアウト固定）")]
    [SerializeField] private CameraControlArea[] controlAreas; // 自動生成される4エリア
    [Range(0f, 0.5f)] [SerializeField] private float leftWidthPercent = 0.15f;
    [Range(0f, 0.5f)] [SerializeField] private float rightWidthPercent = 0.15f;
    [Range(0f, 0.5f)] [SerializeField] private float topHeightPercent = 0.15f;
    [Range(0f, 0.5f)] [SerializeField] private float bottomHeightPercent = 0.15f;
    [Tooltip("生成されるエッジ領域のImageを表示/非表示（Gameビュー上の視覚ガイド）")]
    [SerializeField] private bool showEdgeAreas = true;
    
    public enum MovementAxis { X, Y, Z }
    [Header("移動軸と範囲設定")]
    [SerializeField] private MovementAxis movementAxis = MovementAxis.X; // 移動させる軸
    [Tooltip("選択した軸上で移動できる全長（ワールド単位）")]
    [SerializeField] private float axisRangeLength = 10f;
    [Tooltip("初期位置が範囲の中でどこに位置するか（0 = 最小端, 1 = 最大端）")]
    [Range(0f, 1f)]
    [SerializeField] private float initialOffset = 0.5f;
    
    [Header("移動パラメータ")]
    [SerializeField] private float acceleration = 2f; // 加速度（全方向共通）
    [SerializeField] private float maxSpeed = 5f; // 最大速度
    [SerializeField] private float damping = 0.95f; // 減衰率
    
    [Header("回転パラメータ")]
    [SerializeField] private Vector2 rotationRange = new Vector2(-30f, 30f); // 回転範囲 (度)
    [SerializeField] private float rotationAcceleration = 50f; // 回転加速度
    [SerializeField] private float maxRotationSpeed = 30f; // 最大回転速度
    [SerializeField] private float rotationDamping = 0.95f; // 回転減衰率

    [Header("ヘッドボブ（歩行時の上下揺れ）")]
    [SerializeField] private bool enableHeadBob = true; // ヘッドボブを有効化
    [SerializeField] private float bobAmplitude = 0.05f; // 揺れ幅（m）
    [SerializeField] private float bobFrequency = 6f;    // 揺れ周波数（歩幅感）
    [SerializeField] private float bobSpeedThreshold = 0.1f; // 揺れ開始の速度しきい値
    [SerializeField] private float bobSmoothing = 8f;    // オフセット追従のスムージング係数

    // ランタイム切り替え用API
    public bool EnableHeadBob
    {
        get => enableHeadBob;
        set => enableHeadBob = value;
    }
    
    [Header("デバッグ設定")]
    [SerializeField] private bool enableVisualFeedback = true; // ビジュアルフィードバックを有効にするか
    [SerializeField] private bool showDebugInfo = true; // デバッグ情報を表示するか
    [SerializeField] private bool showDebugPanel = true; // デバッグパネルを表示するか
    [SerializeField] private bool showControlAreas = true; // ControlAreaの表示を有効にするか
    private Color hoverColor = new Color(1f, 1f, 0f, 0.7f); // マウスホバー時の色（黄色半透明）
    // 内部変数
    [HideInInspector] public Vector3 velocity = Vector3.zero;
    private float rotationVelocity = 0f; // 回転速度
    private Vector3 initialPosition;
    private Quaternion initialRotation;
    private Camera targetCamera;
    [SerializeField] private Canvas targetCanvas; // 自動検索されるCanvas

    // 物理（ベース）位置とヘッドボブ用の補間値
    private Vector3 basePosition; // 論理的な基準位置（ヘッドボブを含まない）
    private float bobTimer = 0f;  // サイン波位相
    private float currentBobOffset = 0f; // 現在のYオフセット
    // 軸ごとの実効範囲（min/max）
    private Vector2 axisLimits; // 選択軸における[min, max]

    private bool controllable = true; // Whether this object is currently controllable
#if UNITY_EDITOR
    private bool editorRebuildQueued = false;
#endif
    
    // デバッグ用変数
    private string currentHoveredArea = "None";
    private Vector2 currentMousePos;
    private bool isMouseInAnyArea = false;
    private string debugRectInfo = ""; // RectTransform座標のデバッグ情報
 
    // 移動方向の定義
    public enum MoveDirection
    {
        Left,   // 左
        Right,  // 右
        Up,     // 上
        Down    // 下
    }
    
    // 複数領域制御用の構造体
    [System.Serializable]
    public class CameraControlArea
    {
        [Header("エリア設定")]
        public string areaName = "Control Area"; // エリア名
        public RectTransform rectTransform; // UI要素
        
        [Header("移動設定")]
        public MoveDirection moveDirection = MoveDirection.Right; // 移動方向
        
        [HideInInspector] public Rect screenRect; // 計算されたスクリーン座標
        [HideInInspector] public Image imageComponent; // Imageコンポーネントのキャッシュ
        [HideInInspector] public bool isHovered = false; // ホバー状態
        [HideInInspector] public Color normalColor = Color.white; // 通常時の色（自動取得）
        
        // 回転設定を移動方向に基づいて自動取得
        public bool EnableRotation
        {
            get { return moveDirection == MoveDirection.Up || moveDirection == MoveDirection.Down; }
        }
        
        // 回転軸は常にX軸
        public Vector3 RotationAxis
        {
            get { return Vector3.right; }
        }
        
        // デバッグ色は移動方向に基づいて自動設定
        public Color DebugColor
        {
            get
            {
                switch (moveDirection)
                {
                    case MoveDirection.Left: return Color.red;
                    case MoveDirection.Right: return Color.blue;
                    case MoveDirection.Up: return Color.green;
                    case MoveDirection.Down: return Color.yellow;
                    default: return Color.red;
                }
            }
        }
    }
    
    #endregion

    #region Unity
    void Start()
    {
        // カメラの初期状態を保存
        targetCamera = GetComponent<Camera>();
        if (targetCamera == null)
            targetCamera = Camera.main;
            
        initialPosition = transform.position;
        initialRotation = transform.rotation;
    basePosition = initialPosition; // ベース位置を初期化
    RecalculateAxisLimits();

    // Canvasの自動検索
    if (targetCanvas == null) targetCanvas = FindFirstObjectByType<Canvas>();
    BuildOrUpdateEdgeLayout();
        
        // 操作領域の初期化
        UpdateControlAreas();
        
        // Imageコンポーネントをキャッシュ
        CacheImageComponents();
    }
    
    void Update()
    {
        if (CamAutoMove.IsAutoMoving || ResultManager.IsResultsOpen) return;
        // UpdateControlAreas(); // 毎フレーム領域を更新
        HandleMouseInput();
        ApplyCameraMovement();
        ApplyCameraRotation(); // 回転の適用を追加
        
        // ビジュアルフィードバックの更新
        if (enableVisualFeedback)
        {
            UpdateVisualFeedback();
        }
    }
    
    // Imageコンポーネントをキャッシュ
    #endregion

    #region Area
    private void CacheImageComponents()
    {
        if (controlAreas != null)
        {
            // 複数領域のImageコンポーネントを取得
            foreach (var area in controlAreas)
            {
                if (area.rectTransform != null)
                {
                    area.rectTransform.TryGetComponent<Image>(out area.imageComponent);
                    if (area.imageComponent != null)
                    {
                        area.normalColor = area.imageComponent.color; // 現在の色を通常色として保存
                    }
                }
            }
        }
    }
    
    // ビジュアルフィードバックの更新
    private void UpdateVisualFeedback()
    {
        Vector2 mousePos = Input.mousePosition;
        UpdateMultipleAreasVisual(mousePos);
    }
    
    // 複数領域のビジュアル更新
    private void UpdateMultipleAreasVisual(Vector2 mousePos)
    {
        if (controlAreas == null) return;
        
        RectTransform hoveredRect = null;
        
        // 現在ホバー中の領域を検索
        foreach (var area in controlAreas)
        {
            if (area.rectTransform != null && area.screenRect.Contains(mousePos))
            {
                hoveredRect = area.rectTransform;
                break;
            }
        }
        
        // 全ての領域のホバー状態を更新
        foreach (var area in controlAreas)
        {
            if (area.rectTransform != null && area.imageComponent != null)
            {
                bool shouldHover = (area.rectTransform == hoveredRect);
                
                if (shouldHover && !area.isHovered)
                {
                    // ホバー開始
                    area.isHovered = true;
                    area.imageComponent.color = hoverColor; // 統一されたホバー色を使用
                }
                else if (!shouldHover && area.isHovered)
                {
                    // ホバー終了
                    area.isHovered = false;
                    area.imageComponent.color = area.normalColor;
                }
            }
        }
    }
    
    // 操作領域を更新
    private void UpdateControlAreas()
    {
        if (controlAreas != null && targetCanvas != null)
        {
            foreach (var area in controlAreas)
            {
                if (area.rectTransform != null)
                {
                    area.screenRect = RectTransformToScreenRect(area.rectTransform);
                }
            }
        }
    }

    // EdgeThickness レイアウトを生成/更新（エディタ/ランタイム両対応）
    #endregion

    #region Edge
    public void BuildOrUpdateEdgeLayout()
    {
        if (targetCanvas == null)
        {
            targetCanvas = FindFirstObjectByType<Canvas>();
            if (targetCanvas == null) return;
        }

        var root = targetCanvas.GetComponent<RectTransform>();
        // 4エリアを用意
        RectTransform left = EnsureAreaRect(root, GetAreaObjectName("Left"));
        RectTransform right = EnsureAreaRect(root, GetAreaObjectName("Right"));
        RectTransform top = EnsureAreaRect(root, GetAreaObjectName("Top"));
        RectTransform bottom = EnsureAreaRect(root, GetAreaObjectName("Bottom"));

        // アンカーとサイズ（割合）を設定
        SetupAnchoredPercent(left, new Vector2(0f, 0f), new Vector2(Mathf.Clamp01(leftWidthPercent), 1f));
        SetupAnchoredPercent(right, new Vector2(1f - Mathf.Clamp01(rightWidthPercent), 0f), new Vector2(1f, 1f));
        SetupAnchoredPercent(top, new Vector2(0f, 1f - Mathf.Clamp01(topHeightPercent)), new Vector2(1f, 1f));
        SetupAnchoredPercent(bottom, new Vector2(0f, 0f), new Vector2(1f, Mathf.Clamp01(bottomHeightPercent)));

        // 方向と名前
        EnsureAreaConfig(left, "Left", MoveDirection.Left, new Color(1f, 0.2f, 0.2f, 0.3f));
        EnsureAreaConfig(right, "Right", MoveDirection.Right, new Color(0.2f, 0.6f, 1f, 0.3f));
        EnsureAreaConfig(top, "Up", MoveDirection.Up, new Color(0.3f, 1f, 0.3f, 0.3f));
        EnsureAreaConfig(bottom, "Down", MoveDirection.Down, new Color(1f, 1f, 0.3f, 0.3f));

        // controlAreas を 4 つに設定
        controlAreas = new CameraControlArea[]
        {
            new CameraControlArea { areaName = "Left", rectTransform = left, moveDirection = MoveDirection.Left },
            new CameraControlArea { areaName = "Right", rectTransform = right, moveDirection = MoveDirection.Right },
            new CameraControlArea { areaName = "Up", rectTransform = top, moveDirection = MoveDirection.Up },
            new CameraControlArea { areaName = "Down", rectTransform = bottom, moveDirection = MoveDirection.Down },
        };

    CacheImageComponents();
    ApplyEdgeAreaVisibility();
    UpdateControlAreas();
    }

    private string GetAreaObjectName(string side)
    {
        return $"CamCtrl_Area_{side}_{name}";
    }

    private static RectTransform EnsureAreaRect(RectTransform parent, string objName)
    {
        var t = parent.Find(objName) as RectTransform;
        if (t == null)
        {
            var go = new GameObject(objName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            t = go.GetComponent<RectTransform>();
            t.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.raycastTarget = false; // UIブロックを避ける
        }
        return t;
    }

    private static void SetupAnchoredPercent(RectTransform rt, Vector2 anchorMin, Vector2 anchorMax)
    {
        rt.anchorMin = anchorMin;
        rt.anchorMax = anchorMax;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.pivot = new Vector2(0.5f, 0.5f);
    }

    private static void EnsureAreaConfig(RectTransform rt, string label, MoveDirection dir, Color color)
    {
        rt.gameObject.name = rt.gameObject.name; // keep
        var img = rt.GetComponent<Image>();
        if (img != null)
        {
            img.color = color;
        }
    }

    private void ApplyEdgeAreaVisibility()
    {
        if (targetCanvas == null) return;
        var root = targetCanvas.GetComponent<RectTransform>();
        var names = new[] { GetAreaObjectName("Left"), GetAreaObjectName("Right"), GetAreaObjectName("Top"), GetAreaObjectName("Bottom") };
        foreach (var n in names)
        {
            var t = root.Find(n) as RectTransform;
            if (t == null) continue;
            var img = t.GetComponent<Image>();
            if (img != null) img.enabled = showEdgeAreas;
        }
    }
    
    // RectTransformをスクリーン座標のRectに変換
    private Rect RectTransformToScreenRect(RectTransform rectTransform)
    {
        if (targetCanvas == null) return new Rect();
        
        // RectTransformの画面上の実際の境界を直接計算
        Canvas canvas = targetCanvas;
        Camera cam = canvas.worldCamera ?? targetCamera;
        
        // RectTransformのローカル座標から画面座標を計算
        Vector2 canvasSize = canvas.GetComponent<RectTransform>().sizeDelta;
        
        // Anchorsとsizedelta、anchoredPositionから実際の位置を計算
        Vector2 anchorMin = rectTransform.anchorMin;
        Vector2 anchorMax = rectTransform.anchorMax;
        Vector2 anchoredPos = rectTransform.anchoredPosition;
        Vector2 sizeDelta = rectTransform.sizeDelta;
        
        // 画面上での実際の位置を計算
        float left = anchorMin.x * Screen.width + anchoredPos.x - sizeDelta.x * rectTransform.pivot.x;
        float right = anchorMax.x * Screen.width + anchoredPos.x + sizeDelta.x * (1f - rectTransform.pivot.x);
        float top = anchorMin.y * Screen.height + anchoredPos.y - sizeDelta.y * rectTransform.pivot.y;
        float bottom = anchorMax.y * Screen.height + anchoredPos.y + sizeDelta.y * (1f - rectTransform.pivot.y);
        
        float x = left;
        float y = top;
        float width = right - left;
        float height = bottom - top;
        
        Rect screenRect = new Rect(x, y, width, height);
        
        return screenRect;
    }
    
    // 正規化座標からスクリーン座標のRectに変換（削除予定）
    private Rect ScreenRectFromNormalized(Rect normalizedRect)
    {
        return new Rect(
            normalizedRect.x * Screen.width,
            (1f - normalizedRect.y - normalizedRect.height) * Screen.height,
            normalizedRect.width * Screen.width,
            normalizedRect.height * Screen.height
        );
    }
    
    #endregion

    #region Input
    private void HandleMouseInput()
    {
        if (!controllable) return;
        Vector2 mousePos = Input.mousePosition;
        currentMousePos = mousePos; // デバッグ用に保存
        bool mouseInAnyArea = HandleMultipleAreas(mousePos);
        isMouseInAnyArea = mouseInAnyArea; // デバッグ用に保存
        
        // 領域外では減衰（回転の自動復帰は削除）
        if (!mouseInAnyArea)
        {
            currentHoveredArea = "None"; // デバッグ用
            velocity *= damping;
            rotationVelocity *= rotationDamping; // 回転速度も減衰
        }
    }
    
    // 複数領域の処理 (現在は唯一の処理方法)
    private bool HandleMultipleAreas(Vector2 mousePos)
    {
        if (controlAreas == null || controlAreas.Length == 0) return false;
        
        debugRectInfo = ""; // デバッグ情報をクリア
        
        foreach (var area in controlAreas)
        {
            if (area.rectTransform != null)
            {
                // より詳細なデバッグ情報を追加
                debugRectInfo += $"{area.areaName}: ";
                debugRectInfo += $"Rect({area.screenRect.x:F1}, {area.screenRect.y:F1}, {area.screenRect.width:F1}, {area.screenRect.height:F1}) ";
                debugRectInfo += $"Mouse({mousePos.x:F1}, {mousePos.y:F1}) ";
                debugRectInfo += $"InX:{(mousePos.x >= area.screenRect.x && mousePos.x <= area.screenRect.x + area.screenRect.width)} ";
                debugRectInfo += $"InY:{(mousePos.y >= area.screenRect.y && mousePos.y <= area.screenRect.y + area.screenRect.height)} ";
                debugRectInfo += $"Contains: {area.screenRect.Contains(mousePos)} | ";
                
                if (area.screenRect.Contains(mousePos))
                {
                    currentHoveredArea = area.areaName; // デバッグ用に保存
                    HandleAreaControl(area, mousePos);
                    return true;
                }
            }
        }
        
        return false;
    }
    
    // 個別エリアの制御処理
    private void HandleAreaControl(CameraControlArea area, Vector2 mousePos)
    {
        // 領域内でのマウス位置を -0.5 から 0.5 の範囲に変換
        Vector2 relativePos = new Vector2(
            (mousePos.x - area.screenRect.x) / area.screenRect.width - 0.5f,
            (mousePos.y - area.screenRect.y) / area.screenRect.height - 0.5f
        );
        
        // 移動方向を3Dベクターに変換
        Vector3 worldMoveDirection = GetWorldMoveDirection(area.moveDirection);
        float mouseMagnitude = Mathf.Max(Mathf.Abs(relativePos.x), Mathf.Abs(relativePos.y));
        
        // グローバル座標系での目標位置を計算
        Vector3 targetPosition = CalculateTargetPosition(worldMoveDirection, mouseMagnitude);
        
    // 加速度的に移動（共通の加速度を使用）: 見た目位置でなくベース位置からの差分で計算
    Vector3 direction = (targetPosition - basePosition).normalized;
        velocity += direction * acceleration * Time.deltaTime;
        velocity = Vector3.ClampMagnitude(velocity, maxSpeed);
        
        // カスタム回転を適用
        if (area.EnableRotation)
        {
            // 現在のX回転を取得
            Vector3 currentEuler = transform.eulerAngles;
            // 0-360度を-180-180度に変換
            float currentXRotation = currentEuler.x > 180f ? currentEuler.x - 360f : currentEuler.x;
            
            // Up/Downで目標回転値を計算
            float targetRotation;
            if (area.moveDirection == MoveDirection.Up)
            {
                // Up: mouseMagnitude 0→0.5 を 現在値→rotationRange.x にマッピング
                targetRotation = Mathf.Lerp(currentXRotation, rotationRange.x, mouseMagnitude * 2f);
            }
            else // Down
            {
                // Down: mouseMagnitude 0→0.5 を 現在値→rotationRange.y にマッピング
                targetRotation = Mathf.Lerp(currentXRotation, rotationRange.y, mouseMagnitude * 2f);
            }
            
            // 目標回転への方向を計算
            float rotationDirection = Mathf.Sign(targetRotation - currentXRotation);
            
            // 加速度的に回転速度を変更
            rotationVelocity += rotationDirection * rotationAcceleration * mouseMagnitude * Time.deltaTime;
            rotationVelocity = Mathf.Clamp(rotationVelocity, -maxRotationSpeed, maxRotationSpeed);
        }
    }
    
    // 移動方向enumをワールド座標のVector3に変換
    private Vector3 GetWorldMoveDirection(MoveDirection direction)
    {
        switch (direction)
        {
            case MoveDirection.Left:
                return Vector3.left;   // (-1, 0, 0)
            case MoveDirection.Right:
                return Vector3.right;  // (1, 0, 0)
            case MoveDirection.Up:
                return Vector3.up;     // (0, 1, 0)
            case MoveDirection.Down:
                return Vector3.down;   // (0, -1, 0)
            default:
                return Vector3.right;
        }
    }
    
    // グローバル座標系での目標位置を計算
    private Vector3 CalculateTargetPosition(Vector3 moveDirection, float magnitude)
    {
        // ベース位置を基準に、指定された単一軸のみ目標値を計算
        Vector3 target = basePosition;

        // 方向の符号を、選択軸とエリア方向から決定
        int sign = GetAxisSignFromArea(moveDirection);
        if (sign == 0)
            return target; // 対応しない方向なら変化なし

        float currentAxis = GetAxisValue(basePosition, movementAxis);
        float min = axisLimits.x;
        float max = axisLimits.y;

        float goal = sign > 0 ? Mathf.Lerp(currentAxis, max, magnitude) : Mathf.Lerp(currentAxis, min, magnitude);
        target = SetAxisValue(target, movementAxis, goal);
        return target;
    }
    
    #endregion

    #region Move
    private void ApplyCameraMovement()
    {
        // ベース位置を更新（選択軸のみ）
        // 速度は選択軸の成分のみを使用
        Vector3 axisVel = Vector3.zero;
        switch (movementAxis)
        {
            case MovementAxis.X: axisVel.x = velocity.x; break;
            case MovementAxis.Y: axisVel.y = velocity.y; break;
            case MovementAxis.Z: axisVel.z = velocity.z; break;
        }
        Vector3 delta = axisVel * Time.deltaTime;
        Vector3 newBasePosition = basePosition;

        // 他軸は常に初期位置にロック
        switch (movementAxis)
        {
            case MovementAxis.X:
                newBasePosition.x += delta.x;
                newBasePosition.x = Mathf.Clamp(newBasePosition.x, axisLimits.x, axisLimits.y);
                newBasePosition.y = initialPosition.y;
                newBasePosition.z = initialPosition.z;
                break;
            case MovementAxis.Y:
                newBasePosition.y += delta.y;
                newBasePosition.y = Mathf.Clamp(newBasePosition.y, axisLimits.x, axisLimits.y);
                newBasePosition.x = initialPosition.x;
                newBasePosition.z = initialPosition.z;
                break;
            case MovementAxis.Z:
                newBasePosition.z += delta.z;
                newBasePosition.z = Mathf.Clamp(newBasePosition.z, axisLimits.x, axisLimits.y);
                newBasePosition.x = initialPosition.x;
                newBasePosition.y = initialPosition.y;
                break;
        }

        basePosition = newBasePosition;

        // ヘッドボブのYオフセットを計算して見た目の位置に反映
        float bobY = CalculateHeadBobOffset(Time.deltaTime);
        transform.position = basePosition + new Vector3(0f, bobY, 0f);
        
        // 範囲端に到達したら、その軸の速度を0にする（他軸は常に0）
        switch (movementAxis)
        {
            case MovementAxis.X:
                velocity = new Vector3(Mathf.Abs(newBasePosition.x - axisLimits.x) < 1e-4f || Mathf.Abs(newBasePosition.x - axisLimits.y) < 1e-4f ? 0f : velocity.x, 0f, 0f);
                break;
            case MovementAxis.Y:
                velocity = new Vector3(0f, Mathf.Abs(newBasePosition.y - axisLimits.x) < 1e-4f || Mathf.Abs(newBasePosition.y - axisLimits.y) < 1e-4f ? 0f : velocity.y, 0f);
                break;
            case MovementAxis.Z:
                velocity = new Vector3(0f, 0f, Mathf.Abs(newBasePosition.z - axisLimits.x) < 1e-4f || Mathf.Abs(newBasePosition.z - axisLimits.y) < 1e-4f ? 0f : velocity.z);
                break;
        }
    }

    // ヘッドボブのオフセット（Yのみ）を計算
    private float CalculateHeadBobOffset(float deltaTime)
    {
        // 現在の移動速度の大きさ（ロジック上の速度）
        float speed = velocity.magnitude;

        // スムージング係数を時間に基づき係数化（指数平滑）
        float smooth = 1f - Mathf.Exp(-bobSmoothing * deltaTime);

        if (enableHeadBob && speed > bobSpeedThreshold)
        {
            // 速度に応じて周波数をわずかにスケール
            float freq = bobFrequency * Mathf.Lerp(0.5f, 1f, Mathf.Clamp01(speed / Mathf.Max(0.0001f, maxSpeed)));
            bobTimer += deltaTime * freq;
            // サイン波で目標オフセット
            float target = Mathf.Sin(bobTimer * Mathf.PI * 2f) * bobAmplitude;
            currentBobOffset = Mathf.Lerp(currentBobOffset, target, smooth);
        }
        else
        {
            // 停止/無効時は0へスムーズに収束
            currentBobOffset = Mathf.Lerp(currentBobOffset, 0f, smooth);
            // 位相はリセットしないことで再開時の自然さを維持（必要なら0にしてもOK）
        }

        return currentBobOffset;
    }
    
    private void ApplyCameraRotation()
    {
        // 現在のX回転を取得
        Vector3 currentEuler = transform.eulerAngles;
        float currentXRotation = currentEuler.x > 180f ? currentEuler.x - 360f : currentEuler.x;
        
        // 回転速度を適用
        float newXRotation = currentXRotation + rotationVelocity * Time.deltaTime;
        
        // 回転範囲内に制限
        newXRotation = Mathf.Clamp(newXRotation, rotationRange.x, rotationRange.y);
        
        // 範囲外に達した場合、回転速度を0にする
        if (newXRotation <= rotationRange.x || newXRotation >= rotationRange.y)
        {
            rotationVelocity = 0;
        }
        
        // 新しい回転を適用
        transform.rotation = Quaternion.Euler(newXRotation, currentEuler.y, currentEuler.z);
    }



    // デバッグ用: 操作領域を可視化
    #endregion
    #region API
    public void ChangeControllable(bool isControllable)
    {
        controllable = isControllable;
    }
    #endregion

    #region GUI
    void OnGUI()
    {
        if (!Application.isPlaying) return;
        
        // デバッグ情報を表示
        if (showDebugInfo && showDebugPanel)
        {
            GUILayout.BeginArea(new Rect(10, 10, 400, 300));
            GUILayout.BeginVertical("box");
            
            GUILayout.Label($"Mouse Position: {currentMousePos}");
            GUILayout.Label($"Mouse In Area: {isMouseInAnyArea}");
            GUILayout.Label($"Current Hovered Area: {currentHoveredArea}");
            GUILayout.Label($"Velocity: {velocity}");
            GUILayout.Label($"Camera Position: {transform.position}");
            GUILayout.Label($"Canvas: {(targetCanvas != null ? "Found" : "Not Found")}");
            
            // RectTransform座標情報を表示
            GUILayout.Label("=== Rect Info ===");
            if (!string.IsNullOrEmpty(debugRectInfo))
            {
                string[] rectInfos = debugRectInfo.Split('|');
                foreach (string info in rectInfos)
                {
                    if (!string.IsNullOrEmpty(info.Trim()))
                    {
                        GUILayout.Label(info.Trim());
                    }
                }
            }
            
            GUILayout.EndVertical();
            GUILayout.EndArea();
        }
        
        // 複数領域の表示（設定で制御可能）
        if (showControlAreas && controlAreas != null)
        {
            foreach (var area in controlAreas)
            {
                if (area.rectTransform != null)
                {
                    DrawDebugRect(area.screenRect, area.DebugColor);
                    
                    // エリア名を表示
                    GUI.color = area.DebugColor;
                    GUI.Label(new Rect(area.screenRect.x + 5, area.screenRect.y + 5, 200, 20), area.areaName);
                    GUI.color = Color.white;
                }
            }
        }
    }
    
    // デバッグ用矩形描画
    private void DrawDebugRect(Rect rect, Color color)
    {
        GUI.color = new Color(color.r, color.g, color.b, 0.3f);
        GUI.Box(rect, "");
        GUI.color = Color.white;
    }
    
    // リセット機能
    #endregion

    #region Reset
    [ContextMenu("Reset Camera")]
    public void ResetCamera()
    {
    transform.position = initialPosition;
        transform.rotation = initialRotation;
        velocity = Vector3.zero;
        rotationVelocity = 0f; // 回転速度もリセット
    basePosition = initialPosition;
    bobTimer = 0f;
    currentBobOffset = 0f;
    RecalculateAxisLimits();
        
        // ビジュアルフィードバックもリセット
        ResetVisualFeedback();
    }
    
    // ビジュアルフィードバックのリセット
    private void ResetVisualFeedback()
    {
        // 複数領域の色も元に戻す
        if (controlAreas != null)
        {
            foreach (var area in controlAreas)
            {
                if (area.imageComponent != null && area.isHovered)
                {
                    area.imageComponent.color = area.normalColor;
                    area.isHovered = false;
                }
            }
        }
    }
    
    // スクリプト無効化時の処理
    void OnDisable()
    {
        ResetVisualFeedback();
    }

    // 軸リミットの再計算（初期位置・範囲長・オフセットから算出）
    #endregion

    #region Util
    private void RecalculateAxisLimits()
    {
        float L = Mathf.Max(0f, axisRangeLength);
        float t = Mathf.Clamp01(initialOffset);
        float p0 = GetAxisValue(initialPosition, movementAxis);
        float min = p0 - t * L;
        float max = min + L;
        if (min > max)
        {
            float tmp = min; min = max; max = tmp;
        }
        axisLimits = new Vector2(min, max);
    }

    // エリア方向から選択軸での符号を決める
    private int GetAxisSignFromArea(Vector3 moveDirection)
    {
        switch (movementAxis)
        {
            case MovementAxis.X:
                if (moveDirection.x < -0.1f) return -1; // Left
                if (moveDirection.x > 0.1f) return 1;   // Right
                return 0;
            case MovementAxis.Y:
                if (moveDirection.y < -0.1f) return -1; // Down
                if (moveDirection.y > 0.1f) return 1;   // Up
                return 0;
            case MovementAxis.Z:
                // Up = +Z, Down = -Z とする
                if (moveDirection.y < -0.1f) return -1; // Down -> -Z
                if (moveDirection.y > 0.1f) return 1;   // Up   -> +Z
                return 0;
        }
        return 0;
    }

    // ベクトルの指定軸の値を取得/設定するヘルパ
    private static float GetAxisValue(Vector3 v, MovementAxis axis)
    {
        switch (axis)
        {
            case MovementAxis.X: return v.x;
            case MovementAxis.Y: return v.y;
            case MovementAxis.Z: return v.z;
            default: return v.x;
        }
    }

    private static Vector3 SetAxisValue(Vector3 v, MovementAxis axis, float value)
    {
        switch (axis)
        {
            case MovementAxis.X: v.x = value; break;
            case MovementAxis.Y: v.y = value; break;
            case MovementAxis.Z: v.z = value; break;
        }
        return v;
    }

    // エディタで値を変えた時もリミットを追随（RectTransform変更はOnValidate中に直接行わない）
    private void OnValidate()
    {
        RecalculateAxisLimits();
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            if (!editorRebuildQueued)
            {
                editorRebuildQueued = true;
                EditorApplication.delayCall += () =>
                {
                    if (this == null) return;
                    editorRebuildQueued = false;
                    BuildOrUpdateEdgeLayout();
                    ApplyEdgeAreaVisibility();
                    EditorUtility.SetDirty(this);
                };
            }
            return;
        }
#endif
        // 再生中は即時反映
        BuildOrUpdateEdgeLayout();
        CacheImageComponents();
    }

    // 公開API: コントロールエリアの可視状態を切り替え
    public void SetControlAreasVisible(bool visible)
    {
        showEdgeAreas = visible;
        ApplyEdgeAreaVisibility();
    }

    // 外部（UIボタン等）からの切り替え用
    public void ToggleHeadBob()
#endregion
    {
        enableHeadBob = !enableHeadBob;
    }
}
