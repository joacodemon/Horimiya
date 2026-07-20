using System;

namespace Horimiya.Utils
{
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class InjectableAttribute : Attribute
    {
        public bool IsSingleton { get; set; }

        public InjectableAttribute(bool isSingleton = true)
        {
            IsSingleton = isSingleton;
        }
    }
}
