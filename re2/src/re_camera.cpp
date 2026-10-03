#include "re_camera.hpp"
#include <cstring>
#include <reframework/API.hpp>

using reframework::API;

namespace rcsk
{
	namespace
	{
		API::ManagedObject *invoke_object(API::ManagedObject *obj, const char *method)
		{
			if (obj == nullptr)
				return nullptr;
			const reframework::InvokeRet r = obj->invoke(method, {});
			return r.exception_thrown ? nullptr : static_cast<API::ManagedObject *>(r.ptr);
		}
	}

	bool read_camera(CameraPose &out)
	{
		const auto &api = API::get();
		static API::Method *get_main_view = [] {
			API::TypeDefinition *t = API::get()->tdb()->find_type("via.SceneManager");
			return t ? t->find_method("get_MainView") : nullptr;
		}();
		void *scene_manager = api->get_native_singleton("via.SceneManager");
		if (get_main_view == nullptr || scene_manager == nullptr)
			return false;
		auto *view = get_main_view->call<API::ManagedObject *>(api->get_vm_context(), scene_manager);
		API::ManagedObject *camera = invoke_object(view, "get_PrimaryCamera");
		API::ManagedObject *object = invoke_object(camera, "get_GameObject");
		API::ManagedObject *transform = invoke_object(object, "get_Transform");
		if (transform == nullptr)
			return false;

		const reframework::InvokeRet fov = camera->invoke("get_FOV", {});
		const reframework::InvokeRet pos = transform->invoke("get_Position", {});
		const reframework::InvokeRet rot = transform->invoke("get_Rotation", {});
		if (fov.exception_thrown || pos.exception_thrown || rot.exception_thrown)
			return false;
		// via.vec3 and via.Quaternion come back by value in the return buffer: x, y, z (, w).
		std::memcpy(out.pos, pos.bytes.data(), sizeof(out.pos));
		std::memcpy(out.rot, rot.bytes.data(), sizeof(out.rot));
		out.fov = fov.f;
		return true;
	}
}
