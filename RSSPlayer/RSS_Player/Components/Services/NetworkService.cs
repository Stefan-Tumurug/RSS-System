using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;

namespace RssPlayer.Components.Services
{
    public class NetworkService
    {
        private readonly LoggingService _logger;
        private string _cachedMacAddress;

        public NetworkService(LoggingService logger)
        {
            try
            {
                _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing NetworkService: {ex.Message}");
                throw;
            }
        }

        public string GetMacAddress()
        {
            try
            {
                if (!string.IsNullOrEmpty(_cachedMacAddress))
                {
                    return _cachedMacAddress;
                }

                List<NetworkInterface> activeInterfaces = GetActiveNetworkInterfaces();

                if (activeInterfaces.Count == 0)
                {
                    _logger.LogWarning("No suitable active network interfaces found");
                    return "00-00-00-00-00-00";
                }

                NetworkInterface selectedInterface = SelectNetworkInterface(activeInterfaces);
                return ExtractAndCacheMacAddress(selectedInterface);
            }
            catch (Exception ex)
            {
                _logger.LogError("Comprehensive error getting MAC address", ex);
                return "00-00-00-00-00-00";
            }
        }

        private List<NetworkInterface> GetActiveNetworkInterfaces()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(nic =>
                        nic.OperationalStatus == OperationalStatus.Up &&
                        nic.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                        !IsExcludedInterface(nic))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error retrieving network interfaces", ex);
                return new List<NetworkInterface>();
            }
        }

        private bool IsExcludedInterface(NetworkInterface nic)
        {
            try
            {
                string[] excludedDescriptions = { "Hyper-V", "Bluetooth", "Virtual", "WSL" };
                return excludedDescriptions.Any(desc => nic.Description.Contains(desc));
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error checking interface {nic?.Description ?? "unknown"}", ex);
                return false;
            }
        }

        private NetworkInterface SelectNetworkInterface(List<NetworkInterface> activeInterfaces)
        {
            try
            {
                return activeInterfaces.FirstOrDefault(nic => nic.NetworkInterfaceType == NetworkInterfaceType.Ethernet)
                       ?? activeInterfaces.First();
            }
            catch (Exception ex)
            {
                _logger.LogError("Error selecting network interface", ex);
                return activeInterfaces.FirstOrDefault();
            }
        }

        private string ExtractAndCacheMacAddress(NetworkInterface selectedInterface)
        {
            try
            {
                byte[] macBytes = selectedInterface.GetPhysicalAddress().GetAddressBytes();

                if (macBytes.Length == 0)
                {
                    _logger.LogWarning("Could not get MAC address bytes");
                    return "00-00-00-00-00-00";
                }

                _cachedMacAddress = string.Join("-", macBytes.Select(b => b.ToString("X2")));
                _logger.Log($"Detected MAC Address: {_cachedMacAddress} (Interface: {selectedInterface.Description})");
                return _cachedMacAddress;
            }
            catch (Exception ex)
            {
                _logger.LogError("Error extracting MAC address", ex);
                return "00-00-00-00-00-00";
            }
        }
    }
}