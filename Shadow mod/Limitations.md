# Limitations

Being a game from 2016 (and an engine from way before that), dynamic shadow casting in DS3 has many limitations (and bugs).

Before I explain limitations and bugs, I will briefly explain how shadows work in DS3.

## Baked light maps

DS3's static shadows are implemented using baked light maps in the vanilla game. This has multiple advantages:

-   Cheap. The entire light calculation is already done, and determining which parts of an object are in shadow is as simple as looking up a few pixels in a texture.
-   Distance. Since they are so cheap to do in game, baked light maps are applied to the whole area, not just the parts near the player. This means that even extremely distant objects have shadows.
-   Raytraced. Since these light maps are computed only once on FromSoft's machines, they can use raytracing to accurately simulate light and how it bounces in a scene.

However, their main disadvantage is resolution. The light map for the entire area has to be stored somehow. DS3 stores them as multiple textures. However, these textures are quite low res. This doesn't matter for shadows of distant objects, but it makes the shadows of near objects blurry, blocky, or even disappear.

Dynamic shadows neatly complement baked light maps. It produces sharp shadows for near objects and smoothly fades to baked light maps. From how baked light maps behave, I strongly believe that FromSoft always intended for DS3 to have dynamic shadows.

Shadows cast from far-away objects are soft (blurry) in real life, because the sun (or any light source for that matter) is not a perfect point light source, it has area. A tree is a good example here. While the trunk of a tree casts a hard, sharp shadow on the ground, its leafs cast very soft shadows as they are further away from the ground.

The important thing to note about that, it that soft shadows are essentially blurred and overlap with the sharp outline an object would have if the light source was a perfect point light source. However, this is not how baked light maps in DS3 are. Distant shadows are also soft/blurry, but they are always blurred inward, never outwards.

This can be seen with tree in game. The trunk has a blurry shadow (due to low resolution), but none of its leaves or branches do. Their shadows have been inset so much, that they simply disappeared. **This causes the shadows of an object in baked light maps to always be smaller than the dynamic shadows of that object.** This is what allows this mod to exist at all. If the baked light maps were raytraced normally without this inset blur, objects with dynamic shadows would look weird.

## Dynamic shadows

Dynamic shadows are drawn on top of the once from baked light maps. While they do provide more sharpness to shadows, they are technically purely optional.

### How dynamic shadows work

Shadows are calculated as you would expect from games. The scene is drawn from the perspective of the directional light source, and the depth is stored in a buffer to be used in a deferred rendering pass later. There are 4 levels to this, called 0 to 3. All of these levels are depths buffers of the scene, but at different scales. Level 0 only contains the objects closes to the camera, level 3 contains everything up a certain radius around the camera, and level 1 and 2 are intermediate steps. This setup enables a certain level of detail for shadows. The shadows of close objects are high resolution, while the shadows of far away object are lower resolution. Level 3 also only contains objects until some max distance. The game then fade out dynamic shadows.

This gives us a few dials to play with. We can set the radius around the camera that each level renders. We can also set when the shadow fade out starts and how long it lasts. Since all levels have the same resolution, we must choose the transition points carefully, otherwise it will become very apparent when the relatively lower res shadows of a higher level start. Note that all levels actually have the same resolution, it just that they capture a different area.

We can also change a few other things (like how shadows are softened), but they aren't important right now.

### Max distance

As explained above, shadows are rendered in different levels. However, all objects outside the radius of level 3 cannot have a shadow. This means that really far away objects cannot have a dynamic shadow at all. Even if distant scenery could be improved with shadows, it is not doable.

However, distance also causes another problem. Since all levels have the same resolution, but cover different areas, level 3 has the least pixels per object. This makes far away shadows appear pixely and low res. This is especially apparent with thin tree branches popping in and out of existence as the camera moves away from them.

Unfortunately, there is nothing I can do to make distant shadows better.

### Culling

Directional light sources are though of as infinitely far away, but FromSoft enabled some form of culling for them, for some reason. When an object is too far away, it might be culled from shadow casting, despite its shadow ending up near the camera. Even worse, the cull distance depends on the shadow level. Generally, higher levels have larger cull distances which allows them to contain far away objects, but this causes a problem. As the camera gets closer to the shadow, it will use the data from a lower level that might have culled those objects. This causes the shadows of far away objects to disappear as the camera gets closer in an abrupt and jarring fashion.

This problem forced me to disable shadows for many objects, because they cast long shadows into playable areas.

### Blurring

Shadows are also blurred before being drawn to the screen. This makes them softer and hides the transitions between levels well.

However, FromSoft implemented this too screen-spacey. The blur seem to be applied in screen space without accounting for relative surface angle. This makes dynamic shadows on surfaces that have a low angle relative to the camera appear a lot softer than when looked at directly.

This issue is quite noticeable when looking at grass. The camera typically looks at the ground at the low angle, which makes the shadows of grass almost disappear because of the blur.

Their blur also has another problem. Obviously, blurring shadows needs a filter to only blur shadows on the same surface. However, this filter sometimes causes the shadowed surface immediately behind a surface to be drawn without a shadow. This cause some objects to have a 1px wide halo around them, where the shadows cast on the surface behind them are not drawn. This, again, is quite noticeable with grass (and other foliage).

Unfortunately, there is nothing I can do to fix the blur.

### Chrs not casting shadows

The game enables shadows for characters dynamically depending on their distance to the player character and the camera. Only if both positions are close enough, will the chr shadow be drawn. This sometimes results in comical situations where your player character has to literally touch a chr before they cast a shadow.

Unfortunately, I am not aware of any way to fix that.

### Objects not casting shadows

For some reason, some objects just do not cast shadows. There is a material property that disables shadow casting for parts of an object (or all parts of it), but fixing this would require changing the FLVER files of those objects, which would expand the scope of this mod.

This problem can be seen in many places in Irithyll.

### Objects not showing shadows

For some reason, some objects just do not allow shadows to be drawn on them. The best example of this is in Dreg Heap. The broken tower that crashes into the building with the 2 knights and forms the bridge to Lapp does not draw shadows. It _casts_, but it doesn't even draw its own shadows onto itself.

There are a few other objects like that too.
- The breakable stone piles in Arch Dragon's Peak.
- Some patches of snow in the painted world between the area where you first encounter the old great wolf and the Millwood Knights before the large tower.

Note that this effect is intentional on some surfaces. E.g. swamp slug.

### Objects not being in shadows

Baked light maps being buggy and light sometimes leaking through, FromSoft added an option to make indoor objects to always appear in shadow.

This option either doesn't work sometimes, or I haven't figured out how to use it properly.

The best example of this are the weirdly bright red pieces of fabric in Grand Archives near the Undead Bone Shard.

### LOD

Level of detail for geometry is generally a problem in DS3, but it's even worse for shadows. Even though an object might be for away from the camera, its shadow might be very close to it. Unfortunately, the same low LOD model is used for shadow casting, which forced me to turn off shadow casting for some object completely.

### Shadow Params

Shadow params determine how shadows are rendered. They control everything from blurring, over depth bias, to the radius of levels.

While it is possible to set them per light source, they seem to be bugged. Setting shadow params in some GPARAMs completely breaks dynamic shadows. Some GPARAMs completely ignore shadow params.

The single most buggy area in that regard is m33 (Road of Sacrifices/Farron Keep). Unfortunately, I was unable to use my improved shadow params in that area.

### Incomplete geometry

Some objects had some surfaces culled from the model. This means that some objects have holes in them or one-way wall that the player can normally see, but the light source can. This causes very weird shadows that look awful.

There are soooo many instances of this in every map of the game. The more I looked around, the more [this clip](https://youtu.be/WgA2oGFh2iU?t=1017) resonated with me.

### Incorrect light directions

The light direction of baked shadow maps and dynamic light source is only the same in about half of all maps. For the rest, I had to manually determine the correct angle and patch the GPARAMs.

### Incorrect baked light maps

Some baked light maps are just bugged. If you see strange dark patches (hehe) around the game, there is nothing I can do about it.

### Shadow-only objects not working

Most indoor areas are surrounded by shadow-only boxes to block out a light that might be leaking in. They sometimes just don't work. The best example here is the start of m30_01 (Lothric castle), the first room with the elevator above Dancer.

### Shadow-only objects being misplaced

Some shadow-only object do work, but are misplaced. They cause weird shadows from non-existing objects.

The best example here is a misplaced shadow-only object that is supposed to facilitate the shadow of the house in which you pick up Greirat's cell key in High Wall. The shadow-only object is misplaced, which causes the parts of the roof of the house to have weird shadows (this can be seen near the Pos of Man hollow). But even better, the shadow-only object is also incomplete, it only models the front side of the house, not the back or its sides. This causes a strange shadow in the baked light map right at the top of the entrance to Vordt's arena.
