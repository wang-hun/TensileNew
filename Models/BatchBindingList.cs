using System.ComponentModel;

namespace TensileNeW.Models;

public sealed class BatchBindingList<T> : BindingList<T>
{
    public void AddRange(IEnumerable<T> items)
    {
        foreach (T item in items)
        {
            Add(item);
        }
    }

    public void ReplaceWith(IEnumerable<T> items)
    {
        RaiseListChangedEvents = false;
        try
        {
            Clear();
            foreach (T item in items)
            {
                Add(item);
            }
        }
        finally
        {
            RaiseListChangedEvents = true;
        }

        ResetBindings();
    }
}
