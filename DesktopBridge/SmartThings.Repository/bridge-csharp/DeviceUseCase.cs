// REFERENCE ONLY - not compiled into the UWP app yet. See LocationUseCase.cs and ../README.md.

using System;
using System.Collections.Generic;
using SmartThings.Repository; // projected C++/WinRT component

namespace SmartThings.UI.UseCases
{
    /// <summary>
    /// App-facing use case for devices. Subscribes to the projected DeviceRepository and surfaces
    /// updates (cache first, then server) to the app.
    /// </summary>
    public sealed class DeviceUseCase : IDisposable
    {
        private readonly DeviceRepository _repository;

        public DeviceUseCase(RepositoryHub hub)
        {
            _repository = hub.Devices;
            _repository.DevicesChanged += OnDevicesChanged;
        }

        public event EventHandler<IReadOnlyList<Device>> Devices;

        private void OnDevicesChanged(DeviceRepository sender, object args)
        {
            Devices?.Invoke(this, sender.Current);
        }

        public void Dispose()
        {
            _repository.DevicesChanged -= OnDevicesChanged;
        }
    }
}
