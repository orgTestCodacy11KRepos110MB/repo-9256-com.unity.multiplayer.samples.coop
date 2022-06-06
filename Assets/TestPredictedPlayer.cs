using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class TestPredictedPlayer : NetworkBehaviour
{
    [SerializeField]
    float m_Speed = 5;

    [SerializeField]
    int m_StunDurationTicks = 60;

    public struct PredictedTransform : INetworkSerializable
    {
        const double PredictedSigma = 0.0001f; // will vary according to your gameplay

        public Vector3 Position;
        public Tick TickSent;
        private int StunStopTick;

        public bool IsStunned(Tick currentTick)
        {
            return currentTick < StunStopTick;
        }

        public void Stun(int ticksToStun, Tick currentTick)
        {
            StunStopTick = currentTick + ticksToStun;
        }

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref TickSent);
            serializer.SerializeValue(ref StunStopTick);
        }

        public bool Equals(PredictedTransform other)
        {
            return PositionEquals(Position, other.Position);
        }

        public static bool PositionEquals(Vector3 lhs, Vector3 rhs)
        {
            float num1 = lhs.x - rhs.x;
            float num2 = lhs.y - rhs.y;
            float num3 = lhs.z - rhs.z;
            return (double)num1 * (double)num1 + (double)num2 * (double)num2 + (double)num3 * (double)num3 < PredictedSigma; // adapted from Vector3 == operator
        }
    }

    public struct PredictedInput : INetworkSerializable
    {
        // todo use bitfield to serialize this
        public bool Forward;
        public bool Backward;
        public bool StraffLeft;
        public bool StraffRight;
        public bool isSet; // was there any inputs for that tick

        public bool MouseClick;

        public Tick TickSent;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Forward);
            serializer.SerializeValue(ref Backward);
            serializer.SerializeValue(ref StraffLeft);
            serializer.SerializeValue(ref StraffRight);
            serializer.SerializeValue(ref isSet);
            serializer.SerializeValue(ref TickSent);
            serializer.SerializeValue(ref MouseClick);
        }
    }

    public struct Tick : INetworkSerializable, IEquatable<Tick>
    {
        public int Value;

        public Tick(int value)
        {
            Value = value;
        }

        public static implicit operator int(Tick d) => d.Value;
        public static implicit operator Tick(int value) => new Tick() { Value = value };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Value);
        }

        public bool Equals(Tick other)
        {
            return Value == other.Value;
        }

        public override bool Equals(object obj)
        {
            return obj is Tick other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Value;
        }

        public override string ToString()
        {
            return Value.ToString(); // for debug
        }
    }

    NetworkVariable<PredictedTransform> m_ServerTransform = new(); // always late by x ms. GameObject's transform.position contains predicted value
    List<PredictedInput> m_PredictedInputs = new();
    Dictionary<Tick, PredictedTransform> m_PredictedTransforms = new();
    List<PredictedInput> m_ServerReceivedBufferedInput = new();

    private static Tick CurrentLocalTick => NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick;

    void Awake()
    {
        NetworkManager.Singleton.NetworkTickSystem.Tick += NetworkTick;


    }

    public override void OnNetworkSpawn()
    {
        if (IsClient)
        {
            if (IsOwner)
            {
                m_ServerTransform.OnValueChanged += CheckForCorrection;
            }
            else
            {
                m_ServerTransform.OnValueChanged += GhostTransformChanged;
            }
        }

        if (IsServer)
        {
            // TODO would have been great to get some tool to help here, to control multiple clients
            IEnumerator DebugStunEveryXSeconds()
            {
                while (true)
                {
                    yield return new WaitForSeconds(5);
                    var t = m_ServerTransform.Value;
                    t.Stun(m_StunDurationTicks, CurrentLocalTick); // todo this should cause a misprediction...
                    m_ServerTransform.Value = t;
                }
            }
            StartCoroutine(DebugStunEveryXSeconds()); // debug
        }
    }

    void NetworkTick()
    {
        DebugPrint("NetworkTick begin");

        if (IsClient && IsOwner)
        {
            // check for local inputs
            CheckForLocalInput(out var input);

            // save inputs in input history
            m_PredictedInputs.Add(input);

            // send inputs
            SendInputServerRpc(input);

            // predict input on local transform
            var changedTransform = PredictOneTick(input, CurrentLocalTick);

            // save transform in history
            m_PredictedTransforms.Add(changedTransform.TickSent, changedTransform);
        }

        if (IsServer)
        {
            // check for received buffered inputs and move according to that input
            foreach (var input in m_ServerReceivedBufferedInput) // TODO this is not secure and could allow cheating
            {
                var changedTransform = m_ServerTransform.Value;
                var currentTick = input.TickSent;
                // var currentTick = CurrentLocalTick; // TODO should use currentTick and not input's tick, but I keep getting small mispredictions if I do

                MoveTick(input, ref changedTransform, this, currentTick);
                AbilityTick(input, this, currentTick); // not predicted

                m_ServerTransform.Value = changedTransform; // TODO don't change tick value if rest is not changed?
            }

            m_ServerReceivedBufferedInput.Clear();
        }
        DebugPrint("NetworkTick end");
    }

    PredictedTransform PredictOneTick(PredictedInput input, Tick tick)
    {
        var changedTransform = new PredictedTransform() { Position = transform.position };
        MoveTick(input, ref changedTransform, this, tick);
        transform.position = changedTransform.Position;
        return changedTransform;
    }

    [ServerRpc]
    void SendInputServerRpc(PredictedInput input)
    {
        m_ServerReceivedBufferedInput.Add(input);
    }

    void CheckForLocalInput(out PredictedInput input)
    {
        input = new PredictedInput();
        input.TickSent = NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick;
        if (Input.GetKey(KeyCode.W))
        {
            input.Forward = input.isSet = true;
        }

        if (Input.GetKey(KeyCode.S))
        {
            input.Backward = input.isSet = true;
        }

        if (Input.GetKey(KeyCode.A))
        {
            input.StraffLeft = input.isSet = true;
        }

        if (Input.GetKey(KeyCode.D))
        {
            input.StraffRight = input.isSet = true;
        }

        if (Input.GetKey(KeyCode.Mouse0))
        {
            input.MouseClick = input.isSet = true;
        }
    }

    void DebugPrint(string context)
    {
        return;
        var toPrint = context + "\n PredictedInputs: ";
        foreach (var input in m_PredictedInputs)
        {
            toPrint += $"{input.TickSent} ";
        }

        toPrint += "\nPredictedTransforms: ";
        var sorted = m_PredictedTransforms.Values.ToArray().OrderBy(predictedTransform => predictedTransform.TickSent.Value);
        foreach (var predictedTransform in sorted)
        {
            toPrint += $"{predictedTransform.TickSent} ";
        }

        toPrint += $"\n current tick: {CurrentLocalTick}";
        toPrint += $"\n last tick received: {m_ServerTransform.Value.TickSent}";
        Debug.Log(toPrint);
    }

    void GhostTransformChanged(PredictedTransform previousValue, PredictedTransform newValue)
    {
        // todo interpolate
        transform.position = newValue.Position;
    }

    void CheckForCorrection(PredictedTransform previousValue, PredictedTransform newValue)
    {
        var beforePosition = transform.position; // debug;
        // mispredicted?
        DebugPrint("OnServerTransformValueChanged begin");
        var foundInHistory = m_PredictedTransforms.TryGetValue(newValue.TickSent, out var historyTransform);
        if (!(foundInHistory && newValue.Equals(historyTransform)))
        {
            Debug.Log($"mispredicted! found?{foundInHistory} newValue:{newValue.Position} historyTransform:{historyTransform.Position}");
            // if mispredicted, correct
            // replace transform with newValue // todo issue with physics? stop physics for that operation?
            transform.position = newValue.Position;

            // go through inputs and apply on transform
            foreach (var oneInputFromHistory in m_PredictedInputs)
            {
                var oneTick = oneInputFromHistory.TickSent;
                if (oneTick > newValue.TickSent) // TODO there has to be something more efficient than this
                {
                    var newTransform = PredictOneTick(oneInputFromHistory, oneTick);
                    m_PredictedTransforms[oneTick] = newTransform;
                }
            }
        }

        DebugPrint("OnServerTransformValueChanged before history clean");

        // clear history for acked transforms and inputs
        for (var i = m_PredictedInputs.Count - 1; i >= 0; i--)
        {
            if (m_PredictedInputs[i].TickSent <= newValue.TickSent)
            {
                var tickToRemove = m_PredictedInputs[i].TickSent;
                m_PredictedInputs.RemoveAt(i);
                m_PredictedTransforms.Remove(tickToRemove);
            }
        }
        DebugPrint("OnServerTransformValueChanged end");
        if (beforePosition != transform.position)
        {
            Debug.Log("moved");
        }
    }

    static void MoveTick(PredictedInput input, ref PredictedTransform transform, TestPredictedPlayer self, Tick tick)
    {
        // todo issues with sound? with physics?
        var newPos = transform.Position;
        var deltaTime = 1f / NetworkManager.Singleton.NetworkTickSystem.TickRate;

        if (!transform.IsStunned(tick))
        {
            if (input.Forward)
            {
                newPos.z = newPos.z + self.m_Speed * deltaTime;
            }

            if (input.Backward)
            {
                newPos.z = newPos.z - self.m_Speed * deltaTime;
            }

            if (input.StraffLeft)
            {
                newPos.x = newPos.x - self.m_Speed * deltaTime;
            }

            if (input.StraffRight)
            {
                newPos.x = newPos.x + self.m_Speed * deltaTime;
            }
        }
        else
        {
            Debug.Log("can't move, is stunned");
        }

        transform.Position = newPos;
        transform.TickSent = tick;
    }

    TestPredictedPlayer m_OtherPlayer;

    TestPredictedPlayer OtherPlayer
    {
        get
        {
            if (m_OtherPlayer == null)
            {
                foreach (var player in FindObjectsOfType<TestPredictedPlayer>())
                {
                    if (player != this)
                    {
                        m_OtherPlayer = player;
                    }
                }
            }

            return m_OtherPlayer;
        }
    }

    void AbilityTick(PredictedInput input, TestPredictedPlayer self, Tick currentTick)
    {
        if (input.MouseClick && !self.m_ServerTransform.Value.IsStunned(currentTick)) // not predicting this
        {
            var otherTransform = OtherPlayer.m_ServerTransform.Value;
            otherTransform.Stun(m_StunDurationTicks, currentTick);
            OtherPlayer.m_ServerTransform.Value = otherTransform;
        }
    }
}
