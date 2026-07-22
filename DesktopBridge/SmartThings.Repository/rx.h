#pragma once
//
// Minimal Rx-like primitives (C#/System.Reactive style) for the repository layer.
//
// BehaviorSubject<T> remembers the latest value and *replays it to every new subscriber*.
// That is exactly what lets a repository "return data from the cache on construction": a late
// subscriber (e.g. the UWP UseCase) immediately receives the current value (cache first, then the
// refreshed server value once it arrives).
//
#include <functional>
#include <mutex>
#include <optional>
#include <unordered_map>
#include <vector>

namespace smartthings::rx
{
    // Handle returned by subscribe(); unsubscribes on destruction or when unsubscribe() is called.
    class Subscription
    {
    public:
        Subscription() = default;
        explicit Subscription(std::function<void()> unsubscribe) : unsubscribe_(std::move(unsubscribe)) {}
        Subscription(Subscription&&) noexcept = default;
        Subscription& operator=(Subscription&&) noexcept = default;
        Subscription(const Subscription&) = delete;
        Subscription& operator=(const Subscription&) = delete;
        ~Subscription() { unsubscribe(); }

        void unsubscribe()
        {
            if (unsubscribe_)
            {
                auto fn = std::move(unsubscribe_);
                unsubscribe_ = nullptr;
                fn();
            }
        }

    private:
        std::function<void()> unsubscribe_;
    };

    template <class T>
    class BehaviorSubject
    {
    public:
        using Observer = std::function<void(const T&)>;

        // Subscribe an observer. If a value has already been produced, the observer is invoked
        // immediately with it (replay), then again on every subsequent next().
        [[nodiscard]] Subscription subscribe(Observer observer)
        {
            long id;
            std::optional<T> replay;
            {
                std::lock_guard<std::mutex> lock(mutex_);
                id = nextId_++;
                observers_[id] = observer;
                replay = current_;
            }
            if (replay.has_value())
            {
                observer(*replay);
            }
            return Subscription([this, id] { remove(id); });
        }

        // Push a new value to all observers and remember it for future subscribers.
        void next(const T& value)
        {
            std::vector<Observer> snapshot;
            {
                std::lock_guard<std::mutex> lock(mutex_);
                current_ = value;
                snapshot.reserve(observers_.size());
                for (auto& [id, obs] : observers_) snapshot.push_back(obs);
            }
            for (auto& obs : snapshot) obs(value);
        }

        std::optional<T> value() const
        {
            std::lock_guard<std::mutex> lock(mutex_);
            return current_;
        }

    private:
        void remove(long id)
        {
            std::lock_guard<std::mutex> lock(mutex_);
            observers_.erase(id);
        }

        mutable std::mutex mutex_;
        std::unordered_map<long, Observer> observers_;
        std::optional<T> current_;
        long nextId_ = 1;
    };
}
