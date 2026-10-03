#pragma once

namespace rcsk
{
	struct CameraPose
	{
		float pos[3] = {0, 0, 0};
		float rot[4] = {0, 0, 0, 1}; // x, y, z, w
		float fov = 60.0f;           // degrees, as the engine reports it
	};

	/// Reads RE2's primary camera through REFramework (via.SceneManager -> MainView -> PrimaryCamera). Game thread only.
	bool read_camera(CameraPose &out);
}
