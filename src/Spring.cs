using System;

namespace DynamicIsland
{
    /// <summary>
    /// Critically-damped spring. Stepping is driven by a single shared frame
    /// clock (one tick per rendered frame) so every spring advances together
    /// and geometry is applied exactly once per frame.
    /// </summary>
    internal sealed class Spring
    {
        private readonly Action<double> _apply;
        private readonly double _stiffness;
        private readonly double _damping;
        private readonly double _precision;
        private const int SubSteps = 10;

        private double _value;
        private double _target;
        private double _velocity;
        private bool _active;

        public Spring(Action<double> apply, double stiffness, double damping, double precision = 0.05)
        {
            _apply = apply;
            _stiffness = stiffness;
            _damping = damping;
            _precision = precision;
        }

        public double Value { get { return _value; } }

        public bool IsActive { get { return _active; } }

        public void Set(double value)
        {
            _active = false;
            _value = _target = value;
            _velocity = 0;
            if (_apply != null) _apply(_value);
        }

        public void To(double target)
        {
            if (Math.Abs(target - _value) < 0.0001 && Math.Abs(_velocity) < 0.0001)
            {
                _value = target;
                if (_apply != null) _apply(_value);
                return;
            }
            _target = target;
            _active = true;
        }

        /// <summary>Advance by dt seconds. Returns false once the spring has settled.</summary>
        public bool Step(double dt)
        {
            if (!_active) return false;
            if (dt <= 0.0005) dt = 0.0005;
            if (dt > 0.05) dt = 0.05;

            double sub = dt / SubSteps;
            for (int i = 0; i < SubSteps; i++)
            {
                double springForce = -_stiffness * (_value - _target);
                double dampingForce = -_damping * _velocity;
                _velocity += (springForce + dampingForce) * sub;
                _value += _velocity * sub;
            }

            if (Math.Abs(_value - _target) < _precision && Math.Abs(_velocity) < 0.6)
            {
                _value = _target;
                _velocity = 0;
                _active = false;
                if (_apply != null) _apply(_value);
                return false;
            }

            if (_apply != null) _apply(_value);
            return true;
        }
    }
}
