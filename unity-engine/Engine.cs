// CCSharp --unity-engine: a small UnityEngine, written in the C# subset that CCSharp lowers (and that also runs on .NET, so it can be tested against it).
//
// Objects are arena classes ([MaxInstances(N)], see the README): a reference is a pointer, `null` and `==` work, and the classes derive from one another
// (Component -> Transform, MonoBehaviour, SpriteRenderer, Camera).  A script is a class that derives from MonoBehaviour, as in Unity.
//
// A scene is built by code (the packer writes it): `new GameObject("Hero")`, `go.Attach(component)`.  There is no reflection, so the engine does not look
// for a script's `Update` by name: MonoBehaviour declares the messages `virtual`, and a script overrides them.
//
// Spike: the first slice of the engine (objects, transforms, scripts, sprites, a camera, the frame loop).

using System;
using System.Collections.Generic;

namespace UnityEngine;

class MaxInstancesAttribute : Attribute { public MaxInstancesAttribute(int n) { } }

public static class Time
{
    public static float deltaTime;
    public static float time;
}

/// <summary>What Destroy and Instantiate work on.  Abstract: its N is only the default for the classes that derive from it.</summary>
[MaxInstances(256)]
public abstract class Object
{
    public bool destroyed;

    /// <summary>Called when the object is destroyed (a script's OnDestroy).  It is also what gives the hierarchy a type to ask about: `as` needs a virtual.</summary>
    public virtual void OnDestroy() { }
}

/// <summary>Gives an arena object's slot back (the compiler writes the call: nothing is freed on .NET, whose GC does it).</summary>
public static class Arena
{
    public static void Free<T>(T o) where T : Object { }
}

/// <summary>Builds a GameObject (with its components) the way the prefab it stands for does.  The packer writes a subclass for each prefab.</summary>
[MaxInstances(64)]
public abstract class Prefab
{
    public abstract GameObject Create();
}

/// <summary>Base of everything attached to a GameObject.</summary>
public abstract class Component : Object
{
    public GameObject gameObject;
    public Transform transform;

    /// <summary>Called when the component is attached to a GameObject: where a kind of component registers itself with the engine.</summary>
    public virtual void OnAttach() { }

    public T GetComponent<T>() where T : Component { return gameObject.GetComponent<T>(); }

    /// <summary>A new GameObject like the prefab's (the template carries its Prefab).</summary>
    public GameObject Instantiate(GameObject original)
    {
        if (original == null || original.prefab == null) return null;
        GameObject g = original.prefab.Create();
        g.prefab = original.prefab;
        return g;
    }

    public GameObject Instantiate(GameObject original, Vector3 position, Quaternion rotation)
    {
        GameObject g = Instantiate(original);
        if (g == null) return null;
        g.transform.position = position;
        g.transform.rotation = rotation;
        return g;
    }

    public void Destroy(Object o) { Engine.Destroy(o); }
}

[MaxInstances(256)]
public class GameObject : Object
{
    public Prefab prefab;               // set on a prefab's template and on what it makes: Instantiate(template) calls prefab.Create()
    public string name;
    public int layer;
    public bool activeSelf;
    public Transform transform;
    public List<Component> components;

    public GameObject(string n)
    {
        name = n;
        activeSelf = true;
        components = new List<Component>();
        transform = new Transform();
        transform.localScale = new Vector3(1f, 1f, 1f);
        transform.localRotation = Quaternion.identity;
        Attach(transform);
        Engine.objects.Add(this);
    }

    public void Attach(Component c)
    {
        c.gameObject = this;
        c.transform = transform;
        components.Add(c);
        c.OnAttach();
    }

    public void SetActive(bool value) { activeSelf = value; }

    /// <summary>The first component of type T (or derived from it), else null.  The compiler lowers `c as T` to a type test on the object itself.</summary>
    public T GetComponent<T>() where T : Component
    {
        foreach (Component c in components)
        {
            T t = c as T;
            if (t != null) return t;
        }
        return null;
    }

    public T AddComponent<T>() where T : Component, new()
    {
        T c = new T();
        Attach(c);
        return c;
    }
}

public class Transform : Component
{
    public Vector3 localPosition;
    public Vector3 localScale;
    public Quaternion localRotation;
    public Transform parent;

    /// <summary>The scale in the world: the parents' scales multiplied in (not a shear: a rotated parent with a non-uniform scale is approximated).</summary>
    public Vector3 lossyScale
    {
        get
        {
            if (parent == null) return localScale;
            Vector3 ps = parent.lossyScale;
            return new Vector3(ps.x * localScale.x, ps.y * localScale.y, ps.z * localScale.z);
        }
    }

    public Quaternion rotation
    {
        get
        {
            if (parent == null) return localRotation;
            Quaternion pr = parent.rotation;
            return pr * localRotation;
        }
        set
        {
            if (parent == null) localRotation = value;
            else
            {
                Quaternion inv = Quaternion.Inverse(parent.rotation);
                localRotation = inv * value;
            }
        }
    }

    /// <summary>A point of this transform's space in the world.</summary>
    public Vector3 TransformPoint(Vector3 p)
    {
        Vector3 s = lossyScale;
        Quaternion q = rotation;
        Vector3 scaled = new Vector3(p.x * s.x, p.y * s.y, p.z * s.z);
        Vector3 r = q * scaled;
        Vector3 origin = position;
        return origin + r;
    }

    /// <summary>The world position: the parents' scale, rotation and position applied to the local one.</summary>
    public Vector3 position
    {
        get
        {
            if (parent == null) return localPosition;
            return parent.TransformPoint(localPosition);
        }
        set
        {
            if (parent == null)
            {
                localPosition = value;
                return;
            }
            Quaternion inv = Quaternion.Inverse(parent.rotation);
            Vector3 rel = value - parent.position;
            Vector3 d = inv * rel;
            Vector3 s = parent.lossyScale;
            float sx = s.x; float sy = s.y; float sz = s.z;
            if (sx == 0f) sx = 1f;
            if (sy == 0f) sy = 1f;
            if (sz == 0f) sz = 1f;
            localPosition = new Vector3(d.x / sx, d.y / sy, d.z / sz);
        }
    }

    public void Translate(Vector3 delta) { position = position + delta; }

    /// <summary>Turns about the z axis, in degrees (the 2D rotation).</summary>
    public void RotateZ(float degrees)
    {
        Quaternion turn = Quaternion.Euler(0f, 0f, degrees);
        Quaternion cur = localRotation;
        localRotation = cur * turn;
    }
}

/// <summary>A script.  Unity finds its messages by name; here they are virtual, and the packer writes `override` on the ones a script declares.</summary>
public abstract class MonoBehaviour : Component
{
    public bool enabled;
    public bool started;

    public virtual void Awake() { }
    public virtual void Start() { }
    public virtual void Update() { }
    public virtual void LateUpdate() { }
    public virtual void FixedUpdate() { }
    public virtual void OnCollisionEnter2D(Collision2D c) { }
    public virtual void OnCollisionStay2D(Collision2D c) { }
    public virtual void OnCollisionExit2D(Collision2D c) { }
    public virtual void OnTriggerEnter2D(Collider2D c) { }
    public virtual void OnTriggerStay2D(Collider2D c) { }
    public virtual void OnTriggerExit2D(Collider2D c) { }

    public override void OnAttach()
    {
        enabled = true;
        Engine.behaviours.Add(this);
    }
}

public class SpriteRenderer : Component
{
    public bool enabled;
    public int spriteId;                // the host's texture index
    public float halfW, halfH;          // the sprite's half size in world units at scale 1; the draw multiplies in the transform's scale
    public Color color;
    public int sortingOrder;
    public int sortingLayer;            // the index of the sorting layer in the project's list

    public override void OnAttach()
    {
        enabled = true;
        Engine.sprites.Add(this);
    }
}

public class Camera : Component
{
    public float orthographicSize;
    public Color background;

    public override void OnAttach() { Engine.mainCamera = this; }
}

/// <summary>The scene's objects and the frame loop.</summary>
public static class Engine
{
    public static List<GameObject> objects = new List<GameObject>();
    public static List<MonoBehaviour> behaviours = new List<MonoBehaviour>();
    public static List<SpriteRenderer> sprites = new List<SpriteRenderer>();
    public static Camera mainCamera;
    public static List<Object> doomed = new List<Object>();

    /// <summary>One frame: Awake and Start for the scripts that have not had them, then Update, then LateUpdate.</summary>
    public static void Tick()
    {
        // by index: a script may Instantiate during its Update, and the new behaviours join the list (they get Awake and Start before their first Update)
        for (int i = 0; i < behaviours.Count; i++)
        {
            MonoBehaviour b = behaviours[i];
            if (!b.started)
            {
                b.started = true;
                b.Awake();
                b.Start();
            }
        }
        Physics2D.Update(Time.deltaTime);
        for (int i = 0; i < behaviours.Count; i++)
        {
            MonoBehaviour b = behaviours[i];
            if (!b.started)
            {
                b.started = true;
                b.Awake();
                b.Start();
            }
            if (b.enabled && b.gameObject.activeSelf) b.Update();
        }
        for (int i = 0; i < behaviours.Count; i++)
        {
            MonoBehaviour b = behaviours[i];
            if (b.enabled && b.gameObject.activeSelf) b.LateUpdate();
        }
        Time.time += Time.deltaTime;
        Sweep();
    }

    public static void FixedUpdate()
    {
        for (int i = 0; i < behaviours.Count; i++)
        {
            MonoBehaviour b = behaviours[i];
            if (b.started && b.enabled && b.gameObject.activeSelf) b.FixedUpdate();
        }
    }

    static void ForgetPhysics(Component c)
    {
        Rigidbody2D rb = c as Rigidbody2D;
        if (rb != null) Physics2D.RemoveBody(rb);
        Collider2D col = c as Collider2D;
        if (col != null) Physics2D.RemoveCollider(col);
        Joint2D jn = c as Joint2D;
        if (jn != null) Physics2D.RemoveJoint(jn);
    }

    /// <summary>Destroy takes effect at the end of the frame (as in Unity), and the slots go back to their arenas.</summary>
    public static void Destroy(Object o)
    {
        if (o == null || o.destroyed) return;
        o.destroyed = true;
        GameObject g = o as GameObject;
        if (g != null)
        {
            g.activeSelf = false;
            foreach (Component c in g.components) c.destroyed = true;
        }
        doomed.Add(o);
    }

    /// <summary>Takes a GameObject out of the scene's lists without destroying it (a prefab's template is made like any GameObject, and is not part of the scene).</summary>
    public static void Forget(GameObject g)
    {
        RemoveObject(g);
        foreach (Component c in g.components)
        {
            MonoBehaviour mb = c as MonoBehaviour;
            if (mb != null) RemoveBehaviour(mb);
            SpriteRenderer sr = c as SpriteRenderer;
            if (sr != null) RemoveSprite(sr);
            ForgetPhysics(c);
        }
    }

    static void RemoveObject(GameObject g)
    {
        for (int i = 0; i < objects.Count; i++)
        {
            if (objects[i] == g) { objects.RemoveAt(i); return; }
        }
    }

    static void RemoveBehaviour(MonoBehaviour b)
    {
        for (int i = 0; i < behaviours.Count; i++)
        {
            if (behaviours[i] == b) { behaviours.RemoveAt(i); return; }
        }
    }

    static void RemoveSprite(SpriteRenderer s)
    {
        for (int i = 0; i < sprites.Count; i++)
        {
            if (sprites[i] == s) { sprites.RemoveAt(i); return; }
        }
    }

    static void Sweep()
    {
        while (doomed.Count > 0)
        {
            Object o = doomed[doomed.Count - 1];
            doomed.RemoveAt(doomed.Count - 1);
            GameObject g = o as GameObject;
            if (g != null)
            {
                Forget(g);
                if (mainCamera != null && mainCamera.gameObject == g) mainCamera = null;
                foreach (Component c in g.components) { c.OnDestroy(); Arena.Free(c); }
                Arena.Free(g);
            }
            else
            {
                Component c = o as Component;
                if (c != null && c.gameObject != null)
                {
                    GameObject owner = c.gameObject;
                    for (int i = 0; i < owner.components.Count; i++)
                    {
                        if (owner.components[i] == c) { owner.components.RemoveAt(i); break; }
                    }
                }
                MonoBehaviour mb = o as MonoBehaviour;
                if (mb != null) RemoveBehaviour(mb);
                SpriteRenderer sr = o as SpriteRenderer;
                if (sr != null) RemoveSprite(sr);
                if (c != null) ForgetPhysics(c);
                o.OnDestroy();
                Arena.Free(o);
            }
        }
    }

    public static GameObject Find(string name)
    {
        foreach (GameObject o in objects)
        {
            if (o.name == name) return o;
        }
        return null;
    }

    // ---- for the host (a window that draws the frame): a snapshot of what is visible, then flat reads of it ----
    static List<SpriteRenderer> drawList = new List<SpriteRenderer>();

    /// <summary>Fills the draw list: the sprites that draw this frame, back to front (sorting order, then z).  Returns how many.</summary>
    public static int HostCollect()
    {
        drawList.Clear();
        foreach (SpriteRenderer s in sprites)
        {
            if (s.enabled && s.gameObject.activeSelf) drawList.Add(s);
        }
        for (int i = 1; i < drawList.Count; i++)
        {
            SpriteRenderer v = drawList[i];
            int j = i - 1;
            while (j >= 0 && Before(v, drawList[j]))
            {
                drawList[j + 1] = drawList[j];
                j--;
            }
            drawList[j + 1] = v;
        }
        return drawList.Count;
    }

    static bool Before(SpriteRenderer a, SpriteRenderer b)
    {
        if (a.sortingLayer != b.sortingLayer) return a.sortingLayer < b.sortingLayer;
        if (a.sortingOrder != b.sortingOrder) return a.sortingOrder < b.sortingOrder;
        return a.transform.position.z > b.transform.position.z;
    }

    /// <summary>Field k of draw i: 0 x, 1 y, 2 half width, 3 half height (scaled), 4 r, 5 g, 6 b, 7 a, 8 z, 9..12 m00 m01 m10 m11.</summary>
    public static float HostDrawF(int i, int k)
    {
        SpriteRenderer s = drawList[i];
        Vector3 p = s.transform.position;
        if (k == 0) return p.x;
        if (k == 1) return p.y;
        if (k == 2) return s.halfW * MathF.Abs(s.transform.lossyScale.x);
        if (k == 3) return s.halfH * MathF.Abs(s.transform.lossyScale.y);
        if (k == 4) return s.color.r;
        if (k == 5) return s.color.g;
        if (k == 6) return s.color.b;
        if (k == 7) return s.color.a;
        if (k >= 9) {
            Quaternion q = s.transform.rotation;      // local XY -> world XY (the upper-left 2x2 of the rotation)
            if (k == 9) return 1f - 2f * (q.y * q.y + q.z * q.z);
            if (k == 10) return 2f * (q.x * q.y - q.z * q.w);
            if (k == 11) return 2f * (q.x * q.y + q.z * q.w);
            return 1f - 2f * (q.x * q.x + q.z * q.z);
        }
        return p.z;
    }

    /// <summary>Field k of draw i: 0 texture, 1 sorting order, 2 sorting layer, 3 the GameObject's layer.</summary>
    public static int HostDrawI(int i, int k)
    {
        SpriteRenderer s = drawList[i];
        if (k == 0) return s.spriteId;
        if (k == 1) return s.sortingOrder;
        if (k == 2) return s.sortingLayer;
        return s.gameObject.layer;
    }

    /// <summary>Field k of the main camera: 0 x, 1 y, 2 half height, 3 r, 4 g, 5 b.  False when there is no camera.</summary>
    public static float HostCameraF(int k)
    {
        Vector3 p = mainCamera.transform.position;
        if (k == 0) return p.x;
        if (k == 1) return p.y;
        if (k == 2) return mainCamera.orthographicSize;
        if (k == 3) return mainCamera.background.r;
        if (k == 4) return mainCamera.background.g;
        return mainCamera.background.b;
    }

    public static int HostHasCamera() { return mainCamera == null ? 0 : 1; }

    /// <summary>How many sprites the frame draws.</summary>
    public static int CountDraws()
    {
        int n = 0;
        foreach (SpriteRenderer s in sprites)
        {
            if (s.enabled && s.gameObject.activeSelf) n++;
        }
        return n;
    }
}
