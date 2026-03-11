using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RSSAPI.Models;
using RSSAPI.Services;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Diagnostics;

namespace RSSAPI.Controllers
{
    [Route("api/users")]
    [ApiController]
    [Authorize]
    public class UserController(UserService userService) : ControllerBase
    {
        private readonly UserService userService = userService;

        [HttpGet("all")]
        public async Task<IActionResult> GetAllUsers()
        {
            try
            {
                List<User> userList = await userService.GetAllUsersAsync();
                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Items = userList
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetAllUsers: {ex.Message}");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while retrieving users.",
                    Items = new List<User>()
                });
            }
        }

        [HttpGet("claims")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public IActionResult GetClaims()
        {
            try
            {
                List<JwtClaim> userClaims = [.. User.Claims.Select(claim => new JwtClaim { Type = claim.Type, Value = claim.Value })];

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Items = userClaims
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetClaims: {ex.Message}");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while retrieving claims.",
                    Items = new List<JwtClaim>()
                });
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetUserByID(int ID)
        {
            try
            {
                User? retrievedUser = await userService.GetUserByIDAsync(ID);
                if (retrievedUser == null)
                {
                    return NotFound(new
                    {
                        Success = false,
                        ErrorMessage = "User not found",
                        Item = null as object
                    });
                }

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = retrievedUser
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in GetUserByID: {ex.Message}");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while retrieving the user.",
                    Item = null as object
                });
            }
        }
        [HttpPut("{id}/profile")]
        public async Task<IActionResult> UpdateUserProfileAsync(int userID, [FromBody] UpdateUserProfileRequestModel profileRequest)
        {
            try
            {
                bool isUpdateSuccessful = await userService.UpdateUserProfileAsync(
                    userID, profileRequest.Email, profileRequest.FirstName, profileRequest.LastName);

                if (!isUpdateSuccessful)
                {
                    return NotFound(new
                    {
                        Success = false,
                        ErrorMessage = "User not found or update failed",
                        Item = null as object
                    });
                }

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = "User profile updated successfully"
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdateUserProfileAsync: {ex.Message}");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while updating the user profile.",
                    Item = null as object
                });
            }
        }
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
        [HttpPost]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest createRequest)
        {
            try
            {
                if (string.IsNullOrEmpty(createRequest.Username) || string.IsNullOrEmpty(createRequest.Password))
                {
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = "Username and password are required",
                        Item = null as object
                    });
                }

                User newUser = new()
                {
                    Username = createRequest.Username,
                    Email = createRequest.Email,
                    FirstName = createRequest.FirstName,
                    LastName = createRequest.LastName,
                    Role = createRequest.Role ?? "User",
                    IsActive = true
                };

                bool isUserCreated = await userService.CreateUserAsync(newUser, createRequest.Password);

                if (!isUserCreated)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = "User creation failed (Username might already exist)",
                        Item = null as object
                    });
                }

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = "User created successfully"
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in CreateUser: {ex.Message}");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while creating the user.",
                    Item = null as object
                });
            }
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserRequest updateRequest)
        {
            try
            {
                User updatedUser = new()
                {
                    UserID = id,
                    Email = updateRequest.Email,
                    FirstName = updateRequest.FirstName,
                    LastName = updateRequest.LastName,
                    Role = updateRequest.Role ?? "User",
                    IsActive = updateRequest.IsActive
                };

                bool isUpdateSuccessful = await userService.UpdateUserAsync(updatedUser);

                if (!isUpdateSuccessful)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = "User update failed",
                        Item = null as object
                    });
                }

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = "User updated successfully"
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in UpdateUser: {ex.Message}");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while updating the user.",
                    Item = null as object
                });
            }
        }

        [HttpPost("{id}/password")]
        public async Task<IActionResult> ChangePassword(int id, [FromBody] ChangePasswordRequest passwordRequest)
        {
            try
            {
                if (string.IsNullOrEmpty(passwordRequest.NewPassword))
                {
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = "New password is required",
                        Item = null as object
                    });
                }

                bool isPasswordChanged = await userService.ChangePasswordAsync(id, passwordRequest.NewPassword);

                if (!isPasswordChanged)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = "Password change failed",
                        Item = null as object
                    });
                }

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = "Password changed successfully"
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ChangePassword: {ex.Message}");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while changing the password.",
                    Item = null as object
                });
            }
        }

        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme, Roles = "Admin")]
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            try
            {
                bool isDeletionSuccessful = await userService.DeleteUserAsync(id);

                if (!isDeletionSuccessful)
                {
                    return BadRequest(new
                    {
                        Success = false,
                        ErrorMessage = "User deletion failed",
                        Item = null as object
                    });
                }

                return Ok(new
                {
                    Success = true,
                    ErrorMessage = string.Empty,
                    Item = "User deleted successfully"
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in DeleteUser: {ex.Message}");
                return BadRequest(new
                {
                    Success = false,
                    ErrorMessage = "An error occurred while deleting the user.",
                    Item = null as object
                });
            }
        }
    }

    public class CreateUserRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Role { get; set; }
    }

    public class UpdateUserRequest
    {
        public int UserID { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Role { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class ChangePasswordRequest
    {
        public string NewPassword { get; set; } = string.Empty;
    }
}