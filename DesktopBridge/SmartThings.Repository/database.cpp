#include "database.h"

#include <stdexcept>
#include <winsqlite/winsqlite3.h>

namespace smartthings
{
    namespace
    {
        std::string columnText(sqlite3_stmt* stmt, int col)
        {
            const unsigned char* text = sqlite3_column_text(stmt, col);
            return text ? reinterpret_cast<const char*>(text) : std::string{};
        }
    }

    Database::Database(const std::string& path)
    {
        if (sqlite3_open(path.c_str(), &db_) != SQLITE_OK)
        {
            throw std::runtime_error("Failed to open SQLite database at " + path);
        }
        exec("CREATE TABLE IF NOT EXISTS locations (id TEXT PRIMARY KEY, name TEXT NOT NULL);");
        exec("CREATE TABLE IF NOT EXISTS devices (id TEXT PRIMARY KEY, name TEXT NOT NULL, location_id TEXT);");
    }

    Database::~Database()
    {
        if (db_) sqlite3_close(db_);
    }

    void Database::exec(const char* sql)
    {
        char* error = nullptr;
        if (sqlite3_exec(db_, sql, nullptr, nullptr, &error) != SQLITE_OK)
        {
            std::string message = error ? error : "unknown SQLite error";
            sqlite3_free(error);
            throw std::runtime_error(message);
        }
    }

    LocationList Database::readLocations()
    {
        LocationList result;
        sqlite3_stmt* stmt = nullptr;
        if (sqlite3_prepare_v2(db_, "SELECT id, name FROM locations ORDER BY name;", -1, &stmt, nullptr) == SQLITE_OK)
        {
            while (sqlite3_step(stmt) == SQLITE_ROW)
            {
                result.push_back({columnText(stmt, 0), columnText(stmt, 1)});
            }
        }
        sqlite3_finalize(stmt);
        return result;
    }

    void Database::replaceLocations(const LocationList& locations)
    {
        exec("BEGIN TRANSACTION;");
        exec("DELETE FROM locations;");
        sqlite3_stmt* stmt = nullptr;
        if (sqlite3_prepare_v2(db_, "INSERT INTO locations (id, name) VALUES (?, ?);", -1, &stmt, nullptr) == SQLITE_OK)
        {
            for (const auto& location : locations)
            {
                sqlite3_bind_text(stmt, 1, location.id.c_str(), -1, SQLITE_TRANSIENT);
                sqlite3_bind_text(stmt, 2, location.name.c_str(), -1, SQLITE_TRANSIENT);
                sqlite3_step(stmt);
                sqlite3_reset(stmt);
            }
        }
        sqlite3_finalize(stmt);
        exec("COMMIT;");
    }

    DeviceList Database::readDevices()
    {
        DeviceList result;
        sqlite3_stmt* stmt = nullptr;
        if (sqlite3_prepare_v2(db_, "SELECT id, name, location_id FROM devices ORDER BY name;", -1, &stmt, nullptr) == SQLITE_OK)
        {
            while (sqlite3_step(stmt) == SQLITE_ROW)
            {
                result.push_back({columnText(stmt, 0), columnText(stmt, 1), columnText(stmt, 2)});
            }
        }
        sqlite3_finalize(stmt);
        return result;
    }

    void Database::replaceDevices(const DeviceList& devices)
    {
        exec("BEGIN TRANSACTION;");
        exec("DELETE FROM devices;");
        sqlite3_stmt* stmt = nullptr;
        if (sqlite3_prepare_v2(db_, "INSERT INTO devices (id, name, location_id) VALUES (?, ?, ?);", -1, &stmt, nullptr) == SQLITE_OK)
        {
            for (const auto& device : devices)
            {
                sqlite3_bind_text(stmt, 1, device.id.c_str(), -1, SQLITE_TRANSIENT);
                sqlite3_bind_text(stmt, 2, device.name.c_str(), -1, SQLITE_TRANSIENT);
                sqlite3_bind_text(stmt, 3, device.locationId.c_str(), -1, SQLITE_TRANSIENT);
                sqlite3_step(stmt);
                sqlite3_reset(stmt);
            }
        }
        sqlite3_finalize(stmt);
        exec("COMMIT;");
    }
}
