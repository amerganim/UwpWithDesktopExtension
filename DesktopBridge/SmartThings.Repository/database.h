#pragma once
//
// Thin SQLite cache over the OS-provided SQLite (winsqlite3). Holds the last-known locations and
// devices so the repository can serve data from cache immediately on startup.
//
#include <string>

#include "models.h"

struct sqlite3;

namespace smartthings
{
    class Database
    {
    public:
        explicit Database(const std::string& path);
        ~Database();

        Database(const Database&) = delete;
        Database& operator=(const Database&) = delete;

        LocationList readLocations();
        void replaceLocations(const LocationList& locations);

        DeviceList readDevices();
        void replaceDevices(const DeviceList& devices);

    private:
        void exec(const char* sql);
        sqlite3* db_ = nullptr;
    };
}
