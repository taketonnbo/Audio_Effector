using System;

namespace AudioEffector.Services
{
    [AttributeUsage(AttributeTargets.Method, Inherited = true, AllowMultiple = false)]
    public class LogDescriptionAttribute : Attribute
    {
        public string Description { get; }

        public LogDescriptionAttribute(string description)
        {
            Description = description;
        }
    }
}
