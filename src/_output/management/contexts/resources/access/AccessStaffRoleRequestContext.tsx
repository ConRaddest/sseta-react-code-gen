"use client"

import {
  ReactNode,
  createContext,
  useContext,
  useState,
} from "react"
import { Api } from "@/services/api.service"
import {
  AccessStaffRoleRequest,
  AccessStaffRoleRequestSearchResponse,
  AccessStaffRoleRequestCreateRequest,
  AccessStaffRoleRequestCreateResponse,
} from "@/types/api.types"
import { FetchRequest, DEFAULT_PAGE_SIZE } from "@sseta/components"

interface AccessStaffRoleRequestContextType {
  // State
  items: AccessStaffRoleRequestSearchResponse[]
  totalRows: number
  lastSearchTerm: string
  lastFetchRequest: FetchRequest | null

  // Operations
  fetchItems: (fetchRequest: FetchRequest, shouldMerge?: boolean) => Promise<void>
  retrieve: (staffRoleRequestId: number) => Promise<AccessStaffRoleRequest | null>
  create: (data: AccessStaffRoleRequestCreateRequest) => Promise<AccessStaffRoleRequestCreateResponse | null>
  destroy: (staffRoleRequestId: number) => Promise<boolean>
  loadMoreItems: () => Promise<void>

  // Refresh
  refresh: () => Promise<void>
}

// Undefined default is intentional — enforced by the useAccessStaffRoleRequest hook below.
const AccessStaffRoleRequestContext = createContext<AccessStaffRoleRequestContextType | undefined>(undefined)

export function AccessStaffRoleRequestProvider({ children }: { children: ReactNode }) {
  const [state, setState] = useState<any>({
    items: [],
    totalRows: 0,
    lastSearchTerm: "",
    lastFetchRequest: {
      pageNumber: 1,
      pageSize: DEFAULT_PAGE_SIZE,
      orderByList: [],
      filterByList: [],
    },
  })

  const fetchItems = async (body: FetchRequest, shouldMerge: boolean = false) => {
    try {
      const response = await Api.ACCESS.StaffRoleRequest.search(body)
      const searchFilter = body.filterByList?.find((f) => f.operator === "search")
      const searchTerm = searchFilter ? (searchFilter.value as string).replace(/\*/g, "") : ""
      setState((prev: any) => ({
        ...prev,
        items: shouldMerge ? [...prev.items, ...(response.data.searchResults || [])] : response.data.searchResults || [],
        totalRows: response.data?.totalRows || 0,
        lastSearchTerm: searchTerm,
        lastFetchRequest: body,
      }))
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  const retrieve = async (staffRoleRequestId: number): Promise<AccessStaffRoleRequest | null> => {
    try {
      const response = await Api.ACCESS.StaffRoleRequest.retrieve(staffRoleRequestId)
      return response.data
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  const create = async (data: AccessStaffRoleRequestCreateRequest): Promise<AccessStaffRoleRequestCreateResponse | null> => {
    try {
      const response = await Api.ACCESS.StaffRoleRequest.create(data)
      if (state.lastFetchRequest) await fetchItems(state.lastFetchRequest)
      return response.data
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  const destroy = async (staffRoleRequestId: number): Promise<boolean> => {
    try {
      await Api.ACCESS.StaffRoleRequest.delete(staffRoleRequestId)
      if (state.lastFetchRequest) await fetchItems(state.lastFetchRequest)
      return true
    } catch (error) {
      console.error(error)
      throw error
    }
  }

  const refresh = async () => { if (state.lastFetchRequest) await fetchItems(state.lastFetchRequest) }
  const loadMoreItems = async () => {
    if (state.lastFetchRequest) {
      await fetchItems({ ...state.lastFetchRequest, pageNumber: state.lastFetchRequest.pageNumber + 1 }, true)
    }
  }

  // No useMemo — the React Compiler handles memoization.
  return (
    <AccessStaffRoleRequestContext.Provider
      value={{
        ...state,
        fetchItems,
        retrieve,
        create,
        destroy,
        loadMoreItems,
        refresh,
      }}
    >
      {children}
    </AccessStaffRoleRequestContext.Provider>
  )
}

// Throws if used outside of AccessStaffRoleRequestProvider to catch missing provider wrapping early.
export function useAccessStaffRoleRequest() {
  const context = useContext(AccessStaffRoleRequestContext)
  if (context === undefined) {
    throw new Error("useAccessStaffRoleRequest must be used within a AccessStaffRoleRequestProvider")
  }
  return context
}
