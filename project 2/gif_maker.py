import open3d as o3d
import numpy as np
import imageio
from pathlib import Path
import os
import sys

def render_model_to_gif(obj_path, output_gif_path):
    print(f"Rendering {obj_path}...")
    try:
        mesh = o3d.io.read_triangle_mesh(str(obj_path), enable_post_processing=True)
        if not mesh.has_vertices():
            print(f"Warning: No vertices found in {obj_path}")
            return
            
        mesh.compute_vertex_normals()
        
        vis = o3d.visualization.Visualizer()
        # Invisible window
        vis.create_window(visible=False, width=600, height=500)
        vis.add_geometry(mesh)
        
        opt = vis.get_render_option()
        opt.background_color = np.asarray([245/255.0, 246/255.0, 248/255.0]) # match UI background
        opt.mesh_show_back_face = True
        
        ctr = vis.get_view_control()
        ctr.set_zoom(0.8)
        
        frames = []
        center = mesh.get_center()
        rot_matrix = mesh.get_rotation_matrix_from_xyz((0, np.pi / 18, 0)) # 10 degrees
        
        for i in range(36):
            mesh.rotate(rot_matrix, center=center)
            vis.update_geometry(mesh)
            vis.poll_events()
            vis.update_renderer()
            
            # Capture
            image = vis.capture_screen_float_buffer(False)
            image = np.asarray(image) * 255.0
            image = image.astype(np.uint8)
            
            frames.append(image)
            
        vis.destroy_window()
        
        imageio.mimsave(output_gif_path, frames, duration=100) # 100ms per frame = 10fps
        print(f"Saved GIF to {output_gif_path}")
    except Exception as e:
        print(f"Failed to render {obj_path}: {e}")

if __name__ == "__main__":
    if len(sys.argv) > 1:
        base_dir = Path(sys.argv[1])
    else:
        base_dir = Path(r"TUIO11_NET-master\bin\Debug\3d models")
        
    if not base_dir.exists():
        print(f"Error: {base_dir} does not exist.")
        sys.exit(1)
        
    for file_path in base_dir.rglob("*.obj"):
        out_path = file_path.with_suffix(".gif")
        if not out_path.exists():
            render_model_to_gif(file_path, out_path)
