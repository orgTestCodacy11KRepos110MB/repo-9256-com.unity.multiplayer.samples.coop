using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class TestPredictedPlayer : NetworkBehaviour
{
    [SerializeField]
    float m_Speed = 5;
    public struct PredictedTransform : INetworkSerializable, IEquatable<PredictedTransform>
    {
        public Vector3 Position;
        public Tick TickSent;
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Position);
            serializer.SerializeValue(ref TickSent);
        }

        public bool Equals(PredictedTransform other)
        {
            return Position.Equals(other.Position) && TickSent.Equals(other.TickSent);
        }

        public override bool Equals(object obj)
        {
            return obj is PredictedTransform other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Position, TickSent);
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

        public Tick TickSent;

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Forward);
            serializer.SerializeValue(ref Backward);
            serializer.SerializeValue(ref StraffLeft);
            serializer.SerializeValue(ref StraffRight);
            serializer.SerializeValue(ref isSet);
            serializer.SerializeValue(ref TickSent);
        }
    }

    NetworkVariable<PredictedTransform> m_ServerTransform = new(); // always late by x ms. GameObject's transform.position contains predicted value

    public struct Tick : INetworkSerializable, IEquatable<Tick>
    {
        int m_Value;

        public Tick(int value)
        {
            m_Value = value;
        }
        public static implicit operator int(Tick d) => d.m_Value;
        public static implicit operator Tick(int value) => new Tick() { m_Value = value };
        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref m_Value);
        }

        public bool Equals(Tick other)
        {
            return m_Value == other.m_Value;
        }

        public override bool Equals(object obj)
        {
            return obj is Tick other && Equals(other);
        }

        public override int GetHashCode()
        {
            return m_Value;
        }
    }
    List<PredictedInput> m_PredictedInputs = new();
    Dictionary<Tick, PredictedTransform> m_PredictedTransforms = new();

    void Awake()
    {
        NetworkManager.Singleton.NetworkTickSystem.Tick += NetworkTick;
        m_ServerTransform.OnValueChanged += OnTransformValueChanged;
    }

    void NetworkTick()
    {
        if (IsClient)
        {
            // check for local inputs
            CheckForLocalInput(out var input);

            // if (IsHost)
            // {
            //     // bypass input sending and just move character
            //     m_ServerReceivedBufferedInput.Add(input);
            // }
            // else
            // {
                // save inputs in input history
                m_PredictedInputs.Add(input);

                // send inputs
                SendInputServerRpc(input);

                // predict input on local transform
                var changedTransform = PredictOneTick(input);

                // save transform in history
                m_PredictedTransforms.Add(changedTransform.TickSent, changedTransform);
            // }
        }

        if (IsServer)
        {
            // check for received buffered inputs and move according to that input
            foreach (var input in m_ServerReceivedBufferedInput) // TODO this is not secure and could allow cheating
            {
                var changedTransform = m_ServerTransform.Value;
                MoveTick(input, ref changedTransform, this);

                m_ServerTransform.Value = changedTransform; // TODO don't change tick value if rest is not changed?
            }
            m_ServerReceivedBufferedInput.Clear();
        }
    }

    PredictedTransform PredictOneTick(PredictedInput input)
    {
        var changedTransform = new PredictedTransform() { Position = transform.position };
        MoveTick(input, ref changedTransform, this);
        transform.position = changedTransform.Position;
        return changedTransform;
    }

    List<PredictedInput> m_ServerReceivedBufferedInput = new();
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
            input.isSet = true;
            input.Forward = true;
        }
        if (Input.GetKey(KeyCode.S))
        {
            input.isSet = true;
            input.Backward = true;
        }
        if (Input.GetKey(KeyCode.A))
        {
            input.isSet = true;
            input.StraffLeft = true;
        }
        if (Input.GetKey(KeyCode.D))
        {
            input.isSet = true;
            input.StraffRight = true;
        }
    }

    void OnTransformValueChanged(PredictedTransform previousValue, PredictedTransform newValue)
    {
        // mispredicted?
        var foundInHistory = m_PredictedTransforms.TryGetValue(newValue.TickSent, out var historyTransform);
        if (!(foundInHistory && newValue.Equals(historyTransform)))
        {
            // if mispredicted, correct
            // correction:
            // replace transform with newValue // todo issue with physics? stop physics for that operation?
            transform.position = newValue.Position;

            // go through inputs and apply on transform
            foreach (var oneInputFromHistory in m_PredictedInputs)
            {
                var oneTick = oneInputFromHistory.TickSent;
                if (oneTick > newValue.TickSent) // TODO there has to be something more efficient than this
                {
                    var newTransform = PredictOneTick(oneInputFromHistory);
                    m_PredictedTransforms[oneTick] = newTransform;
                }
            }
        }
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
    }

    static void MoveTick(PredictedInput input, ref PredictedTransform transform, TestPredictedPlayer self)
    {
        // todo issues with sound? with physics?
        var newPos = transform.Position;
        var deltaTime = 1f / NetworkManager.Singleton.NetworkTickSystem.TickRate;

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

        transform.Position = newPos;
        transform.TickSent = GetLocalTick();
    }

    private static Tick GetLocalTick()
    {
        return NetworkManager.Singleton.NetworkTickSystem.LocalTime.Tick;
    }
}
