#pragma once
#include <string>
#include <vector>

namespace smartthings
{
    struct Location
    {
        std::string id;
        std::string name;
    };

    struct Device
    {
        std::string id;
        std::string name;
        std::string locationId;
    };

    using LocationList = std::vector<Location>;
    using DeviceList = std::vector<Device>;
}
