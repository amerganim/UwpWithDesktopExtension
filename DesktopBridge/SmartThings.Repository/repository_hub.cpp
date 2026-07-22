#include "repository_hub.h"

namespace smartthings
{
    RepositoryHub::RepositoryHub(const std::string& dbPath, std::shared_ptr<IRemoteSource> remote)
        : database_(dbPath), remote_(std::move(remote))
    {
        // Constructing a repository primes its subject from the SQLite cache.
        locationRepository_ = std::make_shared<LocationRepository>(database_, *remote_);
        deviceRepository_ = std::make_shared<DeviceRepository>(database_, *remote_);

        // Register for lifecycle fan-out (IRepository reached via BaseRepository).
        repositories_.push_back(locationRepository_.get());
        repositories_.push_back(deviceRepository_.get());
    }

    void RepositoryHub::onClientReady()
    {
        for (IRepository* repository : repositories_) repository->onClientReady();
    }

    void RepositoryHub::onForeground()
    {
        for (IRepository* repository : repositories_) repository->onForeground();
    }

    void RepositoryHub::onSignIn()
    {
        for (IRepository* repository : repositories_) repository->onSignIn();
    }

    void RepositoryHub::onSignOut()
    {
        for (IRepository* repository : repositories_) repository->onSignOut();
    }
}
