using NzbDrone.Common.Exceptions;

namespace NzbDrone.Core.Indexers.Exceptions
{
    public class UnsupportedCapabilitiesException : NzbDroneException
    {
        public UnsupportedCapabilitiesException(string message, params object[] args)
            : base(message, args)
        {
        }

        public UnsupportedCapabilitiesException(string message)
            : base(message)
        {
        }
    }
}
