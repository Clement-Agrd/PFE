using UnityEngine;
using UnityEngine.AI;

namespace Core.TowerDefense
{
    internal sealed class EnemyRecoil
    {
        private Vector3 _direction;
        private float _remaining;
        private float _speed;
        private float _staggerUntil;

        internal bool IsStaggered { get; private set; }

        internal void Reset()
        {
            _remaining = 0f;
            _staggerUntil = 0f;
            IsStaggered = false;
        }

        internal void Push(NavMeshAgent agent, Vector3 attacker, float strength, bool finisher)
        {
            _direction = Vector3.ProjectOnPlane(agent.transform.position - attacker, Vector3.up).normalized;
            if (_direction.sqrMagnitude < .01f) _direction = -agent.transform.forward;
            float distance = (finisher ? 1.45f : .35f) * strength;
            if (distance >= _remaining || finisher)
            {
                _remaining = distance;
                _speed = distance / (finisher ? .21f : .12f);
            }

            if (!finisher) return;
            IsStaggered = true;
            _staggerUntil = Mathf.Max(_staggerUntil, Time.time + .55f);
            agent.isStopped = true;
        }

        internal void Tick(NavMeshAgent agent, float deltaTime)
        {
            if (_remaining > 0f && deltaTime > 0f)
            {
                Vector3 start = agent.nextPosition;
                float distance = Mathf.Min(_remaining, _speed * deltaTime);
                if (agent.Raycast(start + _direction * distance, out var edge))
                    distance = Mathf.Clamp(Vector3.Dot(edge.position - start, _direction) - .035f, 0f, distance);
                if (distance > 0f) agent.Move(_direction * distance);
                _remaining = distance <= .001f ? 0f : _remaining - distance;
            }

            if (IsStaggered && Time.time >= _staggerUntil)
            {
                IsStaggered = false;
                agent.isStopped = false;
            }
        }
    }
}
