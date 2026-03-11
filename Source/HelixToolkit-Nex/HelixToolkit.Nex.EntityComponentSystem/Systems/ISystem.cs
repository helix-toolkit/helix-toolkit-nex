using System;
using System.Collections.Generic;
using System.Text;

namespace HelixToolkit.Nex.ECS.Systems
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <seealso cref="System.IDisposable" />
    public interface ISystem<in T> : IDisposable
    {
        /// <summary>
        /// Gets or sets whether the current <see cref="ISystem{T}"/> instance should update or not.
        /// </summary>
        bool IsEnabled { get; set; }

        /// <summary>
        /// Updates the system once.
        /// Does nothing if <see cref="IsEnabled"/> is false.
        /// </summary>
        /// <param name="state">The state to use.</param>
        void Update(T state);
    }
}
