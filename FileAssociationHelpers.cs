using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;

namespace Peepr;
// Computer\HKEY_CURRENT_USER\Software\Classes\.webp\OpenWithProgids
// Computer\HKEY_CURRENT_USER\Software\Classes\Microsoft\Windows\CurrentVersion\Explorer\FileExts\.webp\UserChoice +OpenWithList +OpenWithProgIDS

//var CurrentUser = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Explorer\\FileExts\\" + ".webp", true);
//CurrentUser.DeleteSubKey("UserChoice", false);
//CurrentUser.Close();

public enum ExtensionTypeToRegister
{
	Image,
	Video,
}

public static class WindowsNotificationHelper
{
	[DllImport("shell32.dll", SetLastError = true)]
	private static extern void SHChangeNotify(int eventId, int flags, IntPtr item1, IntPtr item2);

	public static void RefreshAssociations()
	{
		SHChangeNotify(0x08000000, 0x0000, IntPtr.Zero, IntPtr.Zero);
	}
}

public static class AppRegistrationHelper
{
	static string FullExePath = AppContext.BaseDirectory + "Peepr.exe";
	static string FullIconPath = AppContext.BaseDirectory + "Images\\Peepr.ico";

	public static void RegisterExtensionsAndApp(ExtensionTypeToRegister extensionTypeToRegister)
	{
		try
		{
			using var userRoot = Registry.CurrentUser;
			// First we register the app && give it a pointer to the capabilities key
			using(var key = userRoot.OpenSubKey(@"Software\RegisteredApplications", true))
			{
				key?.SetValue("Peepr", "Software\\Peepr\\Capabilities");
			}

			// Fill out the capabilities key with the app name, icon, && description
			using(var key = userRoot.CreateSubKey($@"Software\Peepr\Capabilities", true))
			{
				key?.SetValue("ApplicationName", "Peepr");
				key?.SetValue("ApplicationIcon", $"\"{FullExePath}\", 0");
				key?.SetValue("ApplicationDescription", "A simple, fast image and video viewer");

				// Round up all the extensions & register them
				var allExtensions = new List<string>();
				if(extensionTypeToRegister == ExtensionTypeToRegister.Image)
				{
					allExtensions.AddRange(Program.ImageFileExtensions);
				}
				else
				{
					allExtensions.AddRange(Program.VideoFileExtensions);
				}

				using(var faKey = key?.CreateSubKey("FileAssociations", true))
				{
					foreach(var ext in allExtensions)
					{
						// Each extension gets its own key with the extension name trailing in caps
						var extNoDot = ext.Replace('.', ' ').Trim().ToUpperInvariant();
						var extAssocKey = $"Peepr.AssocFile.{extNoDot}";
						var extAssocPath = $@"Software\Classes\{extAssocKey}";
						faKey?.SetValue(ext, extAssocKey);

						// This goes in Software\Classes\<ext> to associate the extension with the app
						// I think this is more relevant to Windows 10 && lower, but it doesn't hurt to have
						using(var classExtenKey = userRoot.CreateSubKey($@"Software\Classes\{ext}", true))
						{
							classExtenKey?.SetValue("", extAssocKey);
							using(var openWithProgKey = classExtenKey?.CreateSubKey("OpenWithProgids", true))
							{
								openWithProgKey?.SetValue(extAssocKey, "");
							}
						}

						// Now write the corresponding extension key
						using(var extRootKey = userRoot.CreateSubKey(extAssocPath, true))
						{
							// This will set the icon for this file type in windows, might want to add a
							// custom icon options in future
							using(var faIconKey = extRootKey?.CreateSubKey("DefaultIcon", true))
							{
								faIconKey?.SetValue("", FullIconPath);
							}

							// Now we set the friendly name for the app && the path to Peepr
							// that the shell should use to open files of this type, simply passing the file path
							// as the only argument
							using(var shellOpenKey = extRootKey?.CreateSubKey(@"shell\open", true))
							{
								shellOpenKey?.SetValue("FriendlyAppName", "Peepr");
								using var shellOpenCmdKey = shellOpenKey?.CreateSubKey("command", true);
								shellOpenCmdKey?.SetValue("", $"\"{FullExePath}\" \"%1\"");
							}
						}
					}
				}
			}
			// Lets get the protocol added now, same deal as with the extensions
			using(var key = userRoot.CreateSubKey($@"Software\Classes\peepr", true))
			{
				key?.SetValue("", $"URL: Peepr Protocol");
				key?.SetValue("URL Protocol", "");
				using(var subKey = key?.CreateSubKey(@"DefaultIcon", true))
				{
					subKey?.SetValue("", $"\"{FullIconPath}\", 0");
				}
				using(var subKey = key?.CreateSubKey(@"shell\open\command", true))
				{
					subKey?.SetValue("", $"\"{FullExePath}\" \"%1\"");
				}
			}
			WindowsNotificationHelper.RefreshAssociations();
		}
		catch(Exception ex)
		{
			Helpers.WriteLogEntry(ex.ToString());
		}
	}

	public static void DeRegisterExtensionsAndApp()
	{
		try
		{
			using var userRoot = Registry.CurrentUser;
			using(var key = userRoot.OpenSubKey(@"Software\RegisteredApplications", true))
			{
				key?.DeleteValue("Peepr", false);
			}
			using(var key = userRoot.OpenSubKey(@"Software", true))
			{
				key?.DeleteValue("Peepr", false);
			}
			var allExtensions = new List<string>();
			allExtensions.AddRange(Program.ImageFileExtensions);
			allExtensions.AddRange(Program.VideoFileExtensions);
			foreach(var ext in allExtensions)
			{
				var extNoDotUpper = ext.Replace('.', ' ').Trim().ToUpperInvariant();
				var extAssociationKey = $"Peepr.AssocFile.{extNoDotUpper}";
				var extAssociationPath = $@"Software\Classes\{extAssociationKey}";

				using(var classExtenKey = userRoot.OpenSubKey($@"Software\Classes\{ext}", true))
				{
					classExtenKey?.DeleteValue("", false);
					using(var openWithProgKey = classExtenKey?.OpenSubKey("OpenWithProgids", true))
					{
						openWithProgKey?.DeleteValue(extAssociationKey, false);
					}
				}

				using(var extRootKey = userRoot.OpenSubKey(extAssociationPath, true))
				{
					using(var faIconKey = extRootKey?.OpenSubKey("DefaultIcon", true))
					{
						faIconKey?.DeleteValue("", false);
					}
					extRootKey?.DeleteValue("shell", false);
				}
			}
			// Lets get the protocol added now, same deal as with the extensions
			using(var key = userRoot.OpenSubKey($@"Software\Classes\", true))
			{
				key?.DeleteValue("peepr", false);
			}
			WindowsNotificationHelper.RefreshAssociations();
		}
		catch(Exception ex)
		{
			Helpers.WriteLogEntry(ex.ToString());
		}
	}
}