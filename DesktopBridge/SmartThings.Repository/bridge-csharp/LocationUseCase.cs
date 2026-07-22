// REFERENCE ONLY - not compiled into the UWP app yet.
//
// This is how the C# UWP app consumes the C++ repository library once the C++/WinRT Windows
// Runtime Component (see ../README.md) projects `SmartThings.Repository` into C#. The UseCase
// subscribes to the projected repository's "Changed" event (the observer side of the subject) and
// re-exposes the data to the app as a simple observable.
//
// Projected (WinRT) surface assumed here:
//   namespace SmartThings.Repository {
//     runtimeclass Location { String Id; String Name; }
//     runtimeclass LocationRepository { event ... LocationsChanged; IVectorView<Location> Current; }
//     runtimeclass RepositoryHub {
//         RepositoryHub(String dbPath);
//         LocationRepository Locations { get; }
//         DeviceRepository Devices { get; }
//         void OnClientReady(); void OnForeground(); void OnSignIn(); void OnSignOut();
//     }
//   }

using System;
using System.Collections.Generic;
using SmartThings.Repository; // projected C++/WinRT component

namespace SmartThings.UI.UseCases
{
    /// <summary>
    /// App-facing use case for locations. The view model subscribes to <see cref="Locations"/> and
    /// is updated when the cache is read and again when the server responds.
    /// </summary>
    public sealed class LocationUseCase : IDisposable
    {
        private readonly LocationRepository _repository;

        public LocationUseCase(RepositoryHub hub)
        {
            _repository = hub.Locations;
            _repository.LocationsChanged += OnLocationsChanged;
        }

        /// <summary>Raised whenever locations change (cache first, then server).</summary>
        public event EventHandler<IReadOnlyList<Location>> Locations;

        private void OnLocationsChanged(LocationRepository sender, object args)
        {
            Locations?.Invoke(this, sender.Current);
        }

        public void Dispose()
        {
            _repository.LocationsChanged -= OnLocationsChanged;
        }
    }
}
