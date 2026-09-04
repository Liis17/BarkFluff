package com.barkfluff.client.adapter

import android.view.View
import java.util.Collections
import java.util.IdentityHashMap
import kotlinx.coroutines.CoroutineScope
import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.Job
import kotlinx.coroutines.SupervisorJob
import kotlinx.coroutines.launch

/**
 * Tracks asynchronous attachment work by view. Recycling a row cancels only the work bound to
 * that row, preventing stale progress/images from being written into a reused holder.
 */
class ViewBoundOperationController {
    private val scope = CoroutineScope(SupervisorJob() + Dispatchers.Main.immediate)
    private val jobs = Collections.synchronizedMap(IdentityHashMap<View, Job>())

    fun launch(view: View, block: suspend CoroutineScope.() -> Unit) {
        cancel(view)
        val job = scope.launch(block = block)
        jobs[view] = job
        job.invokeOnCompletion { jobs.remove(view, job) }
    }

    fun cancel(view: View) {
        jobs.remove(view)?.cancel()
    }

    fun cancelTree(root: View) {
        val matching = synchronized(jobs) {
            jobs.keys.filter { candidate ->
                candidate === root || isDescendant(candidate, root)
            }
        }
        matching.forEach(::cancel)
    }

    fun cancelAll() {
        val current = synchronized(jobs) { jobs.values.toList().also { jobs.clear() } }
        current.forEach(Job::cancel)
    }

    private fun isDescendant(view: View, root: View): Boolean {
        var parent = view.parent
        while (parent is View) {
            if (parent === root) return true
            parent = parent.parent
        }
        return false
    }
}
