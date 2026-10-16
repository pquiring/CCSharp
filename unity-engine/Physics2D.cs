// CCSharp --unity-engine: 2D physics, in the same subset as the rest of the engine (it lowers to C, runs on DotNetAnywhere, and runs on .NET).
//
// A small impulse solver, not Box2D: rigid bodies without rotation (a body's angle is never changed), axis-aligned boxes and circles, gravity,
// restitution and friction, triggers, and the collision messages.  What Unity games of the simple kind need; a Box2D-backed solver would sit behind the
// same classes.
//
//   Rigidbody2D   bodyType (Dynamic, Kinematic, Static), mass, gravityScale, drag, velocity, AddForce, AddForce impulse
//   BoxCollider2D / CircleCollider2D   isTrigger, offset, size / radius, bounciness, friction
//   MonoBehaviour messages   FixedUpdate, OnCollisionEnter2D / Stay / Exit, OnTriggerEnter2D / Stay / Exit
//
// Colliders are axis aligned: a collider's world size is its size times the transform's scale, and the transform's rotation is not applied to it.

using System;
using System.Collections.Generic;

namespace UnityEngine;

public class Rigidbody2D : Component
{
    public RigidbodyType2D bodyType;
    public float mass;
    public float gravityScale;
    public float drag;
    public Vector2 velocity;
    public bool freezeRotation;
    float forceX;
    float forceY;
    // the backend's body (-1 for none) and what was last pushed to it or pulled from it
    public int handle;
    public float lastX;
    public float lastY;
    public float lastVx;
    public float lastVy;
    public float lastMass;
    public int lastType;
    public float lastGravity;
    public float lastDrag;
    public float angularVelocity;       // degrees / s
    float torqueAcc;
    float spinImpulse;
    public float lastAngle;             // degrees
    public float lastAngVel;
    public bool lastFreeze;
    public bool disabled;               // the backend body is switched off (its object is inactive)

    public Rigidbody2D() { mass = 1f; gravityScale = 1f; handle = -1; }

    public override void OnAttach() { Physics2D.bodies.Add(this); }

    /// <summary>A force for the next step (mass divides it).</summary>
    public void AddForce(Vector2 force)
    {
        forceX += force.x;
        forceY += force.y;
    }

    /// <summary>Unity's overload with a mode: Force (0) is a force, Impulse (1) a change of velocity by force / mass.</summary>
    public void AddForce(Vector2 force, ForceMode2D mode)
    {
        if (mode == ForceMode2D.Impulse) AddImpulse(force);
        else AddForce(force);
    }

    public Vector2 linearVelocity
    {
        get { return velocity; }
        set { velocity = value; }
    }

    /// <summary>The z rotation in degrees (Unity's Rigidbody2D.rotation): the owner's.</summary>
    public float rotation
    {
        get { return Physics2D.AngleOf(transform); }
        set { transform.rotation = Quaternion.Euler(0f, 0f, value); }
    }

    public void MoveRotation(float degrees) { rotation = degrees; }

    public void AddTorque(float t) { torqueAcc += t; }

    public void AddTorque(float t, ForceMode2D mode)
    {
        if (mode == ForceMode2D.Impulse) spinImpulse += t;
        else torqueAcc += t;
    }

    public void TakeTorque(out float t, out float impulse)
    {
        t = torqueAcc;
        impulse = spinImpulse;
        torqueAcc = 0f;
        spinImpulse = 0f;
    }

    /// <summary>An immediate change of velocity by impulse / mass.</summary>
    public void AddImpulse(Vector2 impulse)
    {
        if (bodyType != RigidbodyType2D.Dynamic) return;
        velocity = new Vector2(velocity.x + impulse.x / mass, velocity.y + impulse.y / mass);
    }

    public void TakeForce(out float fx, out float fy)
    {
        fx = forceX;
        fy = forceY;
        forceX = 0f;
        forceY = 0f;
    }
}

/// <summary>Base of the shapes.  `kind` is 0 for a box and 1 for a circle.</summary>
public abstract class Collider2D : Component
{
    public bool enabled;
    public bool isTrigger;
    public Vector2 offset;
    public float bounciness;
    public float friction;
    public int kind;                    // 0 box, 1 circle, 2 capsule, 3 polygon
    public int frictionCombine;         // PhysicsMaterialCombine: Average 0, Multiply 1, Minimum 2, Maximum 3
    public int bounceCombine;
    public bool vertical;               // a capsule's direction
    public float sizeX;
    public float sizeY;
    public float radius;
    public Rigidbody2D body;            // the body that moves it (set each step), or null: a static collider
    public int uid;                     // stable while it lives: the backend names it by this
    public int shape;                   // the backend's shape, or -1
    public int staticBody;              // the backend's static body, when it has no Rigidbody2D, else -1
    public float shapeSx;               // the scale the shape was made for
    public float shapeSy;
    public float sig;                   // a digest of what the shape was made from
    public float staticX;               // where the static body was put
    public float staticY;

    public override void OnAttach() { enabled = true; shape = -1; staticBody = -1; uid = Physics2D.NextUid(); Physics2D.colliders.Add(this); }
}

public class BoxCollider2D : Collider2D
{
    public BoxCollider2D() { kind = 0; sizeX = 1f; sizeY = 1f; friction = 0.4f; }

    public Vector2 size
    {
        get { return new Vector2(sizeX, sizeY); }
        set { sizeX = value.x; sizeY = value.y; }
    }
}

public class CapsuleCollider2D : Collider2D
{
    public CapsuleCollider2D() { kind = 2; sizeX = 1f; sizeY = 2f; friction = 0.4f; vertical = true; }

    public Vector2 size
    {
        get { return new Vector2(sizeX, sizeY); }
        set { sizeX = value.x; sizeY = value.y; }
    }
}

/// <summary>A polygon collider: convex pieces (the packer cuts the outline up), each a run of AddPoint calls closed by EndPiece.  Points are in the object's space,
/// the collider's offset already added.</summary>
public class PolygonCollider2D : Collider2D
{
    public List<float> pts;
    public List<int> pieces;
    float minX;
    float minY;
    float maxX;
    float maxY;
    int open;

    public PolygonCollider2D()
    {
        kind = 3;
        friction = 0.4f;
        pts = new List<float>();
        pieces = new List<int>();
    }

    public void AddPoint(float x, float y)
    {
        if (pts.Count == 0) { minX = x; maxX = x; minY = y; maxY = y; }
        if (x < minX) minX = x;
        if (x > maxX) maxX = x;
        if (y < minY) minY = y;
        if (y > maxY) maxY = y;
        pts.Add(x);
        pts.Add(y);
        open += 1;
    }

    public void EndPiece()
    {
        pieces.Add(open);
        open = 0;
        // the managed backend sees the outline's box
        sizeX = maxX - minX;
        sizeY = maxY - minY;
        offset = new Vector2(0.5f * (minX + maxX), 0.5f * (minY + maxY));
    }
}

public class CircleCollider2D : Collider2D
{
    public CircleCollider2D() { kind = 1; radius = 0.5f; friction = 0.4f; }
}


/// <summary>The 2D joints, one class with the fields every kind reads (Unity's, same names); a subclass sets the kind and Unity's defaults.  They need the Box2D backend:
/// the C# backend does not move bodies by joints.  A change to any field makes the backend build the joint again.</summary>
public abstract class Joint2D : Component
{
    public int kind;                    // 0 hinge, 1 distance, 2 spring, 3 fixed, 4 slider, 5 wheel, 6 friction, 7 relative, 8 target
    public bool enabled;
    public Rigidbody2D connectedBody;   // null: the world
    public bool enableCollision;
    public bool autoConfigureConnectedAnchor;
    public Vector2 anchor;
    public Vector2 connectedAnchor;
    public bool useLimits;
    public float lower;                 // degrees (hinge) or metres (slider, wheel)
    public float upper;
    public bool useMotor;
    public float motorSpeed;            // degrees / s (hinge, wheel) or metres / s (slider)
    public float maxMotor;              // torque or force
    public float distance;
    public bool autoConfigureDistance;
    public bool maxDistanceOnly;
    public float frequency;
    public float dampingRatio;
    public float angle;                 // the slider's and the wheel's axis, degrees
    public bool autoConfigureAngle;
    public float maxForce;
    public float maxTorque;
    public float correctionScale;
    public bool autoConfigureOffset;
    public Vector2 linearOffset;
    public float angularOffset;
    public bool autoConfigureTarget;
    public Vector2 target;
    public int handle;                  // the backend's joint, or -1
    public float sig;

    public Joint2D()
    {
        enabled = true;
        handle = -1;
        autoConfigureConnectedAnchor = true;
        autoConfigureDistance = true;
        autoConfigureOffset = true;
        autoConfigureTarget = true;
        distance = 1f;
        maxMotor = 10000f;
        correctionScale = 0.3f;
        sig = -1f;
    }

    public override void OnAttach() { Physics2D.joints.Add(this); }
}

public class HingeJoint2D : Joint2D { public HingeJoint2D() { kind = 0; } }
public class DistanceJoint2D : Joint2D { public DistanceJoint2D() { kind = 1; } }
public class SpringJoint2D : Joint2D { public SpringJoint2D() { kind = 2; frequency = 1f; } }
public class FixedJoint2D : Joint2D { public FixedJoint2D() { kind = 3; } }
public class SliderJoint2D : Joint2D { public SliderJoint2D() { kind = 4; } }
public class WheelJoint2D : Joint2D { public WheelJoint2D() { kind = 5; frequency = 2f; dampingRatio = 0.7f; angle = 90f; } }
public class FrictionJoint2D : Joint2D { public FrictionJoint2D() { kind = 6; } }
public class RelativeJoint2D : Joint2D { public RelativeJoint2D() { kind = 7; maxForce = 10000f; maxTorque = 10000f; } }
public class TargetJoint2D : Joint2D { public TargetJoint2D() { kind = 8; frequency = 5f; dampingRatio = 1f; maxForce = 1000f; } }

/// <summary>What a collision message is given.  One object is reused for every message, so a script must not keep it.</summary>
[MaxInstances(2)]
public class Collision2D
{
    public Collider2D collider;         // the other collider
    public Collider2D otherCollider;    // this object's
    public GameObject gameObject;       // the other object
    public Rigidbody2D rigidbody;       // the other object's body, or null
}

public static class Physics2D
{
    public static List<Rigidbody2D> bodies = new List<Rigidbody2D>();
    public static List<Collider2D> colliders = new List<Collider2D>();
    public static List<Joint2D> joints = new List<Joint2D>();
    public static float gravityX = 0f;
    public static float gravityY = -9.81f;
    public static Vector2 gravity
    {
        get { return new Vector2(gravityX, gravityY); }
        set { gravityX = value.x; gravityY = value.y; }
    }
    public static float fixedDeltaTime = 0.02f;
    static int uids;
    public static int NextUid() { uids += 1; return uids; }
    /// <summary>An object's z rotation in degrees, from its quaternion.</summary>
    public static float AngleOf(Transform t)
    {
        Quaternion q = t.rotation;
        return 114.59156f * MathF.Atan2(q.z, q.w);
    }

    public static Collider2D ByUid(int uid)
    {
        for (int i = 0; i < colliders.Count; i++)
        {
            if (colliders[i].uid == uid) return colliders[i];
        }
        return null;
    }

    static Collision2D scratch;
    static float accumulator;

    // the pairs touching in the last step and in this one: (a, b, trigger)
    static List<Collider2D> prevA = new List<Collider2D>();
    static List<Collider2D> prevB = new List<Collider2D>();
    static List<int> prevT = new List<int>();
    public static List<Collider2D> curA = new List<Collider2D>();
    public static List<Collider2D> curB = new List<Collider2D>();
    public static List<int> curT = new List<int>();

    // the overlap Test found: a normal from a to b, and how deep

    public static void RemoveBody(Rigidbody2D r)
    {
        for (int i = 0; i < bodies.Count; i++)
        {
            if (bodies[i] == r) { bodies.RemoveAt(i); break; }
        }
        PhysicsBackend.ReleaseBody(r);
    }

    public static void RemoveJoint(Joint2D j)
    {
        for (int i = 0; i < joints.Count; i++)
        {
            if (joints[i] == j) { joints.RemoveAt(i); break; }
        }
        PhysicsBackend.ReleaseJoint(j);
    }

    public static void RemoveCollider(Collider2D c)
    {
        for (int i = 0; i < colliders.Count; i++)
        {
            if (colliders[i] == c) { colliders.RemoveAt(i); break; }
        }
        PhysicsBackend.ReleaseCollider(c);
        // a pair that held it is over without a message (the object is going away)
        for (int i = prevA.Count - 1; i >= 0; i--)
        {
            if (prevA[i] == c || prevB[i] == c) { prevA.RemoveAt(i); prevB.RemoveAt(i); prevT.RemoveAt(i); }
        }
    }

    /// <summary>The fixed steps due for this frame: each runs FixedUpdate on the scripts, then the physics.</summary>
    public static void Update(float dt)
    {
        accumulator += dt;
        int steps = 0;
        while (accumulator >= fixedDeltaTime && steps < 5)
        {
            Engine.FixedUpdate();
            Step(fixedDeltaTime);
            accumulator -= fixedDeltaTime;
            steps++;
        }
        if (steps == 5) accumulator = 0f;
    }

    public static bool Active(Collider2D c)
    {
        return c.enabled && c.gameObject.activeSelf && !c.destroyed;
    }

    /// <summary>One step: forces and gravity, movement, then the contacts and their messages.</summary>
    /// <summary>One step: the backend moves the bodies and says which colliders touch (curA, curB, curT), then the messages go out.</summary>
    public static void Step(float dt)
    {
        PhysicsBackend.Step(dt);
        Messages();
    }

    // ---- messages

    static bool Held(List<Collider2D> la, List<Collider2D> lb, List<int> lt, Collider2D a, Collider2D b)
    {
        for (int i = 0; i < la.Count; i++)
        {
            if (la[i] == a && lb[i] == b) return true;
        }
        return false;
    }

    static void Messages()
    {
        if (scratch == null) scratch = new Collision2D();
        for (int i = 0; i < curA.Count; i++)
        {
            bool known = Held(prevA, prevB, prevT, curA[i], curB[i]);
            Send(curA[i], curB[i], curT[i] == 1, known ? 1 : 0);
        }
        for (int i = 0; i < prevA.Count; i++)
        {
            if (!Held(curA, curB, curT, prevA[i], prevB[i])) Send(prevA[i], prevB[i], prevT[i] == 1, 2);
        }
        // this step's pairs are the next step's "last"
        prevA.Clear();
        prevB.Clear();
        prevT.Clear();
        for (int i = 0; i < curA.Count; i++)
        {
            prevA.Add(curA[i]);
            prevB.Add(curB[i]);
            prevT.Add(curT[i]);
        }
    }

    /// <summary>phase 0 Enter, 1 Stay, 2 Exit, to the scripts of both objects.</summary>
    static void Send(Collider2D a, Collider2D b, bool trigger, int phase)
    {
        Deliver(a, b, trigger, phase);
        Deliver(b, a, trigger, phase);
    }

    static void Deliver(Collider2D self, Collider2D other, bool trigger, int phase)
    {
        GameObject g = self.gameObject;
        scratch.collider = other;
        scratch.otherCollider = self;
        scratch.gameObject = other.gameObject;
        scratch.rigidbody = other.body;
        for (int i = 0; i < g.components.Count; i++)
        {
            MonoBehaviour m = g.components[i] as MonoBehaviour;
            if (m == null || !m.enabled) continue;
            if (trigger)
            {
                if (phase == 0) m.OnTriggerEnter2D(other);
                else if (phase == 1) m.OnTriggerStay2D(other);
                else m.OnTriggerExit2D(other);
            }
            else
            {
                if (phase == 0) m.OnCollisionEnter2D(scratch);
                else if (phase == 1) m.OnCollisionStay2D(scratch);
                else m.OnCollisionExit2D(scratch);
            }
        }
    }
}
