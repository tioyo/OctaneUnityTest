using System.Collections.Generic;
using UnityEngine;

[ExecuteInEditMode]
public class ParticleBaker : MonoBehaviour
{
    #region Editable variables

    [SerializeField] ParticleSystem _target;

    #endregion

    #region Private variables

    float _lastUpdateTime = -1;

    #endregion

    #region MonoBehaviour functions

    void OnDisable()
    {
        ReleaseRenderers();
    }

    void OnDestroy()
    {
        ReleaseRenderers();
    }

    void LateUpdate()
    {
        if (_target != null)
        {
            // Rebuild the temporary renderers if the simulation time is
            // updated from the last time.
            if (_lastUpdateTime != _target.time)
            {
                ReleaseRenderers();
                BuildRenderers();
                _lastUpdateTime = _target.time;
            }
        }
        else
        {
            // No target is given: destroy temporary renderers.
            ReleaseRenderers();
        }
    }

    #endregion

    #region Temporary renderer wrangling

    Stack<TempRenderer> _renderers = new Stack<TempRenderer>();

    void ReleaseRenderers()
    {
        while (_renderers.Count > 0)
        {
            var r = _renderers.Pop();

            if (Application.isPlaying)
                Destroy(r.mesh);
            else
                DestroyImmediate(r.mesh);

            r.Release();
        }
    }

    void BuildRenderers()
    {
        // Update the particle array.
        var mainModule = _target.main;
        var maxCount = mainModule.maxParticles;

        if (_particleBuffer == null || _particleBuffer.Length != maxCount)
            _particleBuffer = new ParticleSystem.Particle[maxCount];

        var count = _target.GetParticles(_particleBuffer);

        // Update the input vertex array.
        var rendererModule = _target.GetComponent<ParticleSystemRenderer>();
        var template = rendererModule.mesh;

        template.GetVertices(_vtx_in);
        template.GetNormals(_nrm_in);
        template.GetTangents(_tan_in);
        template.GetUVs(0, _uv0_in);
        template.GetIndices(_idx_in, 0);

        // Clear the output vertex array.
        _vtx_out.Clear();
        _nrm_out.Clear();
        _tan_out.Clear();
        _uv0_out.Clear();
        _idx_out.Clear();

        // Bake the particles.
        for (var i = 0; i < count; i++)
        {
            BakeParticle(i, mainModule.startRotation3D);

            // Flush the current vertex array into a temporary renderer when:
            // - This particle is the last one.
            // - Vertex count is going to go over the 64k limit.
            if (i == count - 1 || _vtx_out.Count + _vtx_in.Count > 65535)
            {
                // Build a mesh with the output vertex buffer.
                var mesh = new Mesh();
                mesh.hideFlags = HideFlags.HideAndDontSave;

                mesh.SetVertices(_vtx_out);
                mesh.SetNormals(_nrm_out);
                mesh.SetTangents(_tan_out);
                mesh.SetUVs(0, _uv0_out);
                mesh.SetTriangles(_idx_out, 0, true);

                // Allocate a temporary renderer and give the mesh.
                var renderer = TempRenderer.Allocate();
                renderer.SetTransform(transform);
                renderer.mesh = mesh;
                renderer.material = rendererModule.sharedMaterial;

                _renderers.Push(renderer);

                // Clear the output vertex array.
                _vtx_out.Clear();
                _nrm_out.Clear();
                _tan_out.Clear();
                _uv0_out.Clear();
                _idx_out.Clear();
            }
        }
    }

    #endregion

    #region Mesh baker

    // Arrays/lists used to bake particles.
    // These arrays/lists are reused between frames to reduce memory pressure.

    ParticleSystem.Particle[] _particleBuffer;

    List<Vector3> _vtx_in = new List<Vector3>();
    List<Vector3> _nrm_in = new List<Vector3>();
    List<Vector4> _tan_in = new List<Vector4>();
    List<Vector2> _uv0_in = new List<Vector2>();
    List<int> _idx_in = new List<int>();

    List<Vector3> _vtx_out = new List<Vector3>();
    List<Vector3> _nrm_out = new List<Vector3>();
    List<Vector4> _tan_out = new List<Vector4>();
    List<Vector2> _uv0_out = new List<Vector2>();
    List<int> _idx_out = new List<int>();

    void BakeParticle(int index, bool useEuler)
    {
        var p = _particleBuffer[index];

        var mtx = Matrix4x4.TRS(
            p.position,
            useEuler ? 
                Quaternion.Euler(p.rotation3D) :
                Quaternion.AngleAxis(p.rotation, p.axisOfRotation),
            Vector3.one * p.GetCurrentSize(_target)
        );

        var vi0 = _vtx_out.Count;

        foreach (var v in _vtx_in) _vtx_out.Add(mtx.MultiplyPoint(v));

        foreach (var n in _nrm_in) _nrm_out.Add(mtx.MultiplyVector(n));

        foreach (var t in _tan_in)
        {
            var mt = mtx.MultiplyVector(t);
            _tan_out.Add(new Vector4(mt.x, mt.y, mt.z, t.w));
        }

        _uv0_out.AddRange(_uv0_in);

        foreach (var idx in _idx_in) _idx_out.Add(idx + vi0);
    }

    #endregion
}
