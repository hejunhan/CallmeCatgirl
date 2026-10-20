using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace CallmeCatgirl.UI
{
    /// <summary>
    /// Standalone 2D physics lab for the RAM install. Clips are hinged L-shaped limit stops;
    /// only the slot contacts clamp the gold fingers with high friction.
    /// </summary>
    public sealed class MemoryInstallPhysicsPrototype : MonoBehaviour
    {
        private const float RamWidth = 7f, RamHeight = 2.2f, NotchWidth = .8f;
        private const float SlotY = -2.1f, ClipX = 4.35f, ClipPivotY = -.5f;
        private const float SeatY = -.825f;
        private const float FrictionTop = SlotY + 1.7f, FrictionBottom = SlotY - .4f;
        // L outer corners: top (1.35,0.95), bottom (-0.18,-0.8). Rotating this segment
        // to vertical gives the open angle where the clip no longer reaches into the slot.
        private const float ClipTopX = 1.35f, ClipTopY = .95f, ClipBottomX = -.18f, ClipBottomY = -.8f;
        private Rigidbody2D ram;
        private Camera view;
        private Text status;
        private Vector2 target, gripLocal;
        private bool dragging, seated;
        private bool example;
        private float seatedAt;
        private Clip leftClip, rightClip;
        private PhysicsMaterial2D highFriction, lowFriction, ramMaterial;
        private static Sprite solid;

        private sealed class Clip
        {
            public Rigidbody2D Body;
            public bool Open;
            public float OpenAngle;
            public Collider2D[] Colliders;
            public bool IsClosed => Mathf.Abs(Mathf.DeltaAngle(Body.rotation, 0)) < 5f;
            public void SetOpen(bool value) => Open = value;
            public void Tick(float dt)
            {
                float target = Open ? OpenAngle : 0f;
                Body.MoveRotation(Mathf.MoveTowardsAngle(Body.rotation, target, 120f * dt));
            }
        }

        private void Awake()
        {
            view = FindAnyObjectByType<Camera>();
            if (view == null)
            {
                var cameraObject = new GameObject("Physics Camera");
                view = cameraObject.AddComponent<Camera>();
                view.orthographic = true; view.orthographicSize = 5; view.transform.position = new Vector3(0, 0, -10);
                view.backgroundColor = new Color32(242, 240, 232, 255); view.clearFlags = CameraClearFlags.SolidColor;
            }
            highFriction = new PhysicsMaterial2D("SlotHighFriction") { friction = 1.6f, bounciness = 0f };
            lowFriction = new PhysicsMaterial2D("ClipLowFriction") { friction = 0f, bounciness = 0f };
            ramMaterial = new PhysicsMaterial2D("RamFriction") { friction = 1.1f, bounciness = 0f };
            BuildGeometry();
            BuildUi();
            SeatExample();
        }

        private void BuildGeometry()
        {
            var background = MakeBox("SlotBack", new Vector2(0, -0.1f), new Vector2(RamWidth + .8f, 2.7f),
                new Color32(225, 222, 214, 255), 0, true, null);
            background.GetComponent<Collider2D>().enabled = false;
            MakeBox("SlotFloor", new Vector2(0, SlotY), new Vector2(RamWidth + 1.2f, .35f),
                new Color32(160, 154, 142, 255), 1, true, highFriction);
            // Side rails only clamp the RAM ends; an edge has no top shelf to support the module.
            MakeRail("LeftRail", -(RamWidth * .5f + .02f));
            MakeRail("RightRail", RamWidth * .5f + .02f);
            MakeBox("CenterPeg", new Vector2(0, SlotY + .18f), new Vector2(NotchWidth - .08f, .7f),
                new Color32(112, 105, 94, 255), 3, true, highFriction);
            // The whole red pocket is high friction: floor + clamp contacts. A solid tint shows the zone.
            MakeFrictionZone(new Vector2(0, SlotY + .95f), new Vector2(RamWidth + .2f, 1.25f));
            MakeBox("DropCatcher", new Vector2(0, -3.7f), new Vector2(18f, .4f),
                new Color32(198, 193, 183, 255), 1, true, null);

            var module = new GameObject("RamModule");
            module.transform.position = new Vector2(0, 1.0f);
            var body = module.AddComponent<Rigidbody2D>();
            body.gravityScale = 1.1f; body.mass = 1; body.linearDamping = 1.4f; body.angularDamping = 1.2f;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            ram = body;

            // DDR5-style key: a bottom-center notch, split into left/right bodies plus a top bridge.
            float sideWidth = (RamWidth - NotchWidth) * .5f, sideX = (RamWidth + NotchWidth) * .25f;
            AddRamPart("LeftBody", new Vector2(-sideX, 0), new Vector2(sideWidth, RamHeight));
            AddRamPart("RightBody", new Vector2(sideX, 0), new Vector2(sideWidth, RamHeight));
            AddRamPart("TopBridge", new Vector2(0, .35f), new Vector2(NotchWidth, 1.5f));

            float openAngle = Mathf.Atan2(ClipTopX - ClipBottomX, ClipTopY - ClipBottomY) * Mathf.Rad2Deg;
            leftClip = BuildClip("LeftClip", -ClipX, openAngle);
            rightClip = BuildClip("RightClip", ClipX, -openAngle);
        }

        private void AddRamPart(string name, Vector2 position, Vector2 size)
        {
            var part = new GameObject(name);
            part.transform.SetParent(ram.transform, false);
            part.transform.localPosition = position;
            part.transform.localScale = new Vector3(size.x, size.y, 1);
            var renderer = part.AddComponent<SpriteRenderer>();
            renderer.sprite = SolidSprite(); renderer.color = new Color32(58, 61, 60, 255); renderer.sortingOrder = 5;
            var collider = part.AddComponent<BoxCollider2D>();
            collider.size = Vector2.one; collider.sharedMaterial = ramMaterial;
        }

        private Clip BuildClip(string name, float x, float openAngle)
        {
            var pivot = new GameObject(name);
            pivot.transform.position = new Vector2(x, ClipPivotY);
            var body = pivot.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic; body.gravityScale = 0;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            // L shape: vertical post + horizontal latch arm reaching inward over the RAM notch.
            AddBar(pivot.transform, "Post", new Vector2(0, .075f), new Vector2(.36f, 1.75f), new Color32(175, 165, 145, 255), 6);
            AddBar(pivot.transform, "Latch", new Vector2(openAngle > 0 ? .75f : -.75f, .70f), new Vector2(1.2f, .5f),
                new Color32(175, 165, 145, 255), 10);
            var polygon = pivot.AddComponent<PolygonCollider2D>();
            polygon.points = openAngle > 0
                ? new[] { new Vector2(-.18f, -.8f), new Vector2(.18f, -.8f), new Vector2(.18f, .65f),
                    new Vector2(1.35f, .65f), new Vector2(1.35f, .95f), new Vector2(-.18f, .95f) }
                : new[] { new Vector2(.18f, -.8f), new Vector2(-.18f, -.8f), new Vector2(-.18f, .65f),
                    new Vector2(-1.35f, .65f), new Vector2(-1.35f, .95f), new Vector2(.18f, .95f) };
            polygon.sharedMaterial = lowFriction;
            body.rotation = openAngle;
            var clip = new Clip
            {
                Body = body, OpenAngle = openAngle,
                Colliders = pivot.GetComponentsInChildren<Collider2D>()
            };
            clip.SetOpen(true);
            return clip;
        }

        private void AddBar(Transform parent, string name, Vector2 position, Vector2 size, Color color, int order)
        {
            var bar = new GameObject(name);
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = position;
            bar.transform.localScale = new Vector3(size.x, size.y, 1);
            var renderer = bar.AddComponent<SpriteRenderer>();
            renderer.sprite = SolidSprite(); renderer.color = color; renderer.sortingOrder = order;
        }

        private void BuildUi()
        {
            var canvasObject = new GameObject("Physics HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            status = new GameObject("Status", typeof(Text)).GetComponent<Text>();
            status.transform.SetParent(canvasObject.transform, false);
            var rect = status.rectTransform;
            rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1); rect.pivot = new Vector2(.5f, 1);
            rect.anchoredPosition = new Vector2(0, -30); rect.sizeDelta = new Vector2(1100, 110);
            status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            status.fontSize = 24; status.alignment = TextAnchor.MiddleCenter; status.color = Color.black;
            status.text = "点击左右 L 型卡扣开合；拖住内存条任意位置，利用重力压入插槽。红区整体高摩擦，中央凹槽要对准凸起。按 R 重置。";
        }

        private static Sprite SolidSprite()
        {
            if (solid == null)
            {
                var texture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                texture.SetPixel(0, 0, Color.white); texture.Apply();
                solid = Sprite.Create(texture, new Rect(0, 0, 1, 1), new Vector2(.5f, .5f), 1f);
            }
            return solid;
        }

        private static GameObject MakeBox(string name, Vector2 position, Vector2 size, Color color, int order,
            bool isStatic, PhysicsMaterial2D material)
        {
            var go = new GameObject(name);
            go.transform.position = position;
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var renderer = go.AddComponent<SpriteRenderer>();
            renderer.sprite = SolidSprite(); renderer.color = color; renderer.sortingOrder = order;
            var collider = go.AddComponent<BoxCollider2D>(); collider.size = Vector2.one; collider.sharedMaterial = material;
            if (isStatic) { var body = go.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Static; }
            return go;
        }

        private void MakeRail(string name, float x)
        {
            var rail = new GameObject(name);
            rail.transform.position = new Vector2(x, SlotY + .55f);
            var edge = rail.AddComponent<EdgeCollider2D>();
            edge.points = new[] { new Vector2(0, -.55f), new Vector2(0, .55f) };
            edge.sharedMaterial = highFriction;
        }

        private static void MakeFrictionZone(Vector2 position, Vector2 size)
        {
            var zone = new GameObject("FrictionZone");
            zone.transform.position = position;
            zone.transform.localScale = new Vector3(size.x, size.y, 1);
            var renderer = zone.AddComponent<SpriteRenderer>();
            renderer.sprite = SolidSprite(); renderer.color = new Color32(205, 65, 55, 80); renderer.sortingOrder = 2;
            var trigger = zone.AddComponent<BoxCollider2D>();
            trigger.isTrigger = true; trigger.size = Vector2.one;
        }

        private void Update()
        {
            if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) ResetModule();
            if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame) SeatExample();
            if (seated) return;
            var mouse = Mouse.current;
            if (mouse == null) return;
            var world = view.ScreenToWorldPoint(mouse.position.ReadValue()); world.z = 0;
            if (mouse.leftButton.wasPressedThisFrame)
            {
                var hit = Physics2D.OverlapPoint(world);
                if (hit != null && hit.attachedRigidbody == leftClip.Body) { leftClip.SetOpen(!leftClip.Open); return; }
                if (hit != null && hit.attachedRigidbody == rightClip.Body) { rightClip.SetOpen(!rightClip.Open); return; }
                if (hit != null && hit.attachedRigidbody == ram)
                {
                    gripLocal = ram.transform.InverseTransformPoint(world);
                    target = world; dragging = true;
                }
            }
            if (dragging && mouse.leftButton.isPressed) target = world;
            if (mouse.leftButton.wasReleasedThisFrame) dragging = false;
        }

        private void FixedUpdate()
        {
            if (ram == null) return;
            leftClip.Tick(Time.fixedDeltaTime); rightClip.Tick(Time.fixedDeltaTime);
            if (dragging)
            {
                // Grip point is fixed in RAM-local space; this is the point the hand actually holds.
                var gripWorld = (Vector2)ram.transform.TransformPoint(gripLocal);
                var pull = target - gripWorld;
                var force = pull * (90f + Mathf.Min(pull.magnitude, 6f) * 160f) - ram.GetPointVelocity(gripWorld) * 12f;
                ram.AddForceAtPosition(Vector2.ClampMagnitude(force, 2000f), gripWorld);
            }
            // The whole tinted pocket is a contact zone, not just the two small pads.
            if (Mathf.Abs(ram.position.x) <= RamWidth * .5f && ram.position.y <= FrictionTop && ram.position.y >= FrictionBottom)
            {
                float keep = Mathf.Clamp01(1f - 6f * Time.fixedDeltaTime);
                ram.linearVelocity *= keep;
                ram.angularVelocity *= keep;
            }
            CheckSeated();
        }

        private void CheckSeated()
        {
            if (seated || example) return;
            float angle = Mathf.Abs(Mathf.DeltaAngle(ram.rotation, 0));
            float x = Mathf.Abs(ram.position.x);
            bool inside = ram.position.y < SlotY + 1.8f && ram.position.y > SlotY - .8f;
            bool slow = ram.linearVelocity.magnitude < .35f && Mathf.Abs(ram.angularVelocity) < 12f;
            bool locked = leftClip.IsClosed && rightClip.IsClosed;
            if (locked && angle < 4f && x < .3f && inside && slow)
            {
                if (seatedAt == 0) seatedAt = Time.time;
                if (Time.time - seatedAt > .25f)
                {
                    seated = true; ram.linearVelocity = Vector2.zero; ram.angularVelocity = 0;
                    if (status != null) status.text = "安装完成。按 R 重新开始。";
                }
            }
            else seatedAt = 0;
        }

        private void SeatExample()
        {
            example = true; seated = false; dragging = false; seatedAt = 0;
            ram.position = new Vector2(0, SeatY); ram.rotation = 0;
            ram.linearVelocity = Vector2.zero; ram.angularVelocity = 0;
            leftClip.SetOpen(false); rightClip.SetOpen(false);
            leftClip.Body.rotation = 0; rightClip.Body.rotation = 0;
            if (status != null) status.text = "示例：RAM 已在插槽内，左右卡扣扣住。点击卡扣打开，再拖 RAM 向上试拔出。T 恢复示例，R 自由落体。";
        }

        private void ResetModule()
        {
            example = false; seated = false; dragging = false; seatedAt = 0;
            ram.position = new Vector2(0, 1.0f); ram.rotation = 0;
            ram.linearVelocity = Vector2.zero; ram.angularVelocity = 0;
            leftClip.SetOpen(true); rightClip.SetOpen(true);
            leftClip.Body.rotation = leftClip.OpenAngle; rightClip.Body.rotation = rightClip.OpenAngle;
            leftClip.Body.linearVelocity = rightClip.Body.linearVelocity = Vector2.zero;
            leftClip.Body.angularVelocity = rightClip.Body.angularVelocity = 0;
            if (status != null) status.text = "点击左右 L 型卡扣开合；拖住内存条任意位置，利用重力压入插槽。红区整体高摩擦，中央凹槽要对准凸起。按 R 重置。";
        }
    }
}
