using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace lospoderosos_lite.Utils
{
    public class DependencyContainer
    {
        private readonly Dictionary<Type, object> _singletons = new Dictionary<Type, object>();
        private readonly Dictionary<Type, Type> _transients = new Dictionary<Type, Type>();

        public void RegisterSingleton<T>(T instance)
        {
            _singletons[typeof(T)] = instance;
        }

        public void RegisterTransient<T>()
        {
            _transients[typeof(T)] = typeof(T);
        }

        public T Resolve<T>()
        {
            return (T)Resolve(typeof(T));
        }

        public object Resolve(Type type)
        {
            object singletonInstance;
            if (_singletons.TryGetValue(type, out singletonInstance))
            {
                return singletonInstance;
            }

            Type implementationType = type;
            Type registeredType;
            if (_transients.TryGetValue(type, out registeredType))
            {
                implementationType = registeredType;
            }

            var constructor = implementationType.GetConstructors()
                .OrderByDescending(c => c.GetParameters().Length)
                .FirstOrDefault();

            if (constructor == null)
            {
                // Try creating instance if no explicit constructor is defined
                try
                {
                    return Activator.CreateInstance(implementationType);
                }
                catch
                {
                    throw new Exception(string.Format("Cannot resolve type {0}. No suitable constructor found.", type.Name));
                }
            }

            var parameters = constructor.GetParameters();
            var resolvedParameters = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                resolvedParameters[i] = Resolve(parameters[i].ParameterType);
            }

            return constructor.Invoke(resolvedParameters);
        }
    }
}
