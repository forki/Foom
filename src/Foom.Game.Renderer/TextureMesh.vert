#version 330 core

in vec3 position;
in vec2 in_uv;
in vec4 in_color;

uniform mat4x4 uni_projection;
uniform mat4x4 uni_view;

out vec2 uv;
out vec4 color;

void main ()
{
	vec4 snapToPixel = uni_projection * uni_view * vec4(position, 1.0);
	vec4 vertex = snapToPixel;

	//vertex.xyz = snapToPixel.xyz / snapToPixel.w;
	//vertex.x = floor(160 * vertex.x) / 160;
	//vertex.y = floor(120 * vertex.y) / 120;
	//vertex.xyz = vertex.xyz * snapToPixel.w;



    gl_Position = vertex;


    uv = in_uv;
	color = in_color;
}
