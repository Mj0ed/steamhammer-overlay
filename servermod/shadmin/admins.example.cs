//-----------------------------------------------------------------------------
// Who may use the admin commands.
//
// Copy this file to admins.cs and put the Steam IDs in it. The check happens
// on the server against the id Steam authenticated the connection with, never
// against anything the client sent — a client can claim any id it likes.
//
// There is no in-game way to add someone here. Editing this file and
// restarting is the only route, which is deliberate: a command that can
// promote an admin is a command worth stealing.
//-----------------------------------------------------------------------------

function SHAdmin_list()
{
   // One SteamID64 per line. Anything else is ignored.
   // These are placeholders — replace them. Do not commit real ids to a
   // public repository: a list of who can run admin commands is a list of
   // who is worth impersonating.
   return
      "76561190000000000" SPC
      "76561190000000001";
}
