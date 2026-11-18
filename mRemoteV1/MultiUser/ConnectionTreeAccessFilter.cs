using BrightIdeasSoftware;
using mRemoteNG.Connection;

namespace mRemoteNG.MultiUser
{
    /// <summary>
    /// Hides from the connection tree every node the current user is not allowed to access.
    /// Nodes are only hidden, never removed from the model, so that saving the
    /// connections to SQL does not delete what the user can't see.
    /// </summary>
    public class ConnectionTreeAccessFilter : IModelFilter
    {
        public bool Filter(object modelObject)
        {
            return MultiUserService.CanAccess(modelObject as ConnectionInfo);
        }
    }

    /// <summary>Logical AND of several filters (null entries are ignored).</summary>
    public class CompositeAllModelFilter : IModelFilter
    {
        private readonly IModelFilter[] _filters;

        public CompositeAllModelFilter(params IModelFilter[] filters)
        {
            _filters = filters;
        }

        public bool Filter(object modelObject)
        {
            foreach (var filter in _filters)
            {
                if (filter != null && !filter.Filter(modelObject))
                    return false;
            }
            return true;
        }
    }
}
